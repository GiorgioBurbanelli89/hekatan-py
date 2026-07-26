// scipy.io EMBEBIDO — loadmat/savemat del formato binario MAT-file v5 (MATLAB Level 5), sin
// Python externo. Soporta arrays numéricos (double/int→double) y strings (mxCHAR), reales, 2D/1D,
// column-major↔row-major, y elementos comprimidos (zlib). Es el formato que usa scipy.io.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Calcpad.Core.Python
{
    internal static class PythonScipyIO
    {
        // miTYPES
        const int miINT8 = 1, miUINT8 = 2, miINT16 = 3, miUINT16 = 4, miINT32 = 5, miUINT32 = 6,
                  miSINGLE = 7, miDOUBLE = 9, miINT64 = 12, miUINT64 = 13, miMATRIX = 14, miCOMPRESSED = 15, miUTF8 = 16;
        // mxCLASS
        const int mxCHAR = 4, mxDOUBLE = 6, mxSINGLE = 7, mxINT8 = 8, mxUINT8 = 9, mxINT16 = 10,
                  mxUINT16 = 11, mxINT32 = 12, mxUINT32 = 13, mxINT64 = 14, mxUINT64 = 15;

        // ======================= LOADMAT =======================
        public static object LoadMat(string path)
        {
            var bytes = File.ReadAllBytes(path);
            var d = new PyDict();
            bool swap = bytes.Length >= 128 && bytes[126] == (byte)'M' && bytes[127] == (byte)'I';
            int pos = 128;   // salta header de 128 bytes
            while (pos + 8 <= bytes.Length)
            {
                var (dtype, data, next) = ReadElement(bytes, pos, swap);
                pos = next;
                byte[] mat = data; bool sw = swap;
                if (dtype == miCOMPRESSED) { mat = Inflate(data); var (dt2, d2, _) = ReadElement(mat, 0, swap); if (dt2 != miMATRIX) continue; mat = d2; }
                else if (dtype != miMATRIX) continue;
                var (name, val) = ParseMatrix(mat, sw);
                if (name != null) d.Set(name, val);
            }
            return d;
        }

        // lee un elemento tagged; devuelve (dtype, datos, posición siguiente alineada a 8)
        private static (int, byte[], int) ReadElement(byte[] b, int pos, bool swap)
        {
            int f = ReadI32(b, pos, swap);
            int hi = (f >> 16) & 0xFFFF;
            if (hi != 0)   // small element format (datos ≤4 bytes van en la palabra siguiente)
            {
                int dt = f & 0xFFFF, nb = hi; var data = new byte[nb];
                Array.Copy(b, pos + 4, data, 0, Math.Min(nb, 4));
                return (dt, data, pos + 8);
            }
            int dtype = f, nbytes = ReadI32(b, pos + 4, swap);
            var dd = new byte[nbytes]; Array.Copy(b, pos + 8, dd, 0, Math.Min(nbytes, b.Length - pos - 8));
            int total = 8 + nbytes; if (total % 8 != 0) total += 8 - (total % 8);   // padding a 8
            return (dtype, dd, pos + total);
        }

        private static (string, object) ParseMatrix(byte[] m, bool swap)
        {
            int p = 0;
            var (_, flags, n1) = ReadElement(m, p, swap); p = n1;              // Array Flags (miUINT32, 8B)
            int cls = flags.Length >= 1 ? flags[0] : 0;                        // clase mx en el 1er byte (LE)
            var (_, dimsB, n2) = ReadElement(m, p, swap); p = n2;              // Dimensions (miINT32)
            int nd = dimsB.Length / 4; var dims = new int[nd];
            for (int i = 0; i < nd; i++) dims[i] = ReadI32(dimsB, i * 4, swap);
            var (_, nameB, n3) = ReadElement(m, p, swap); p = n3;              // Name (miINT8)
            string name = Encoding.ASCII.GetString(nameB).TrimEnd('\0');
            var (prType, prB, _) = ReadElement(m, p, swap);                    // Real part (pr)

            int rows = nd >= 1 ? dims[0] : 1, cols = nd >= 2 ? dims[1] : 1, total = rows * cols;
            if (cls == mxCHAR)   // string
            {
                var sb = new StringBuilder();
                for (int i = 0; i < total; i++) sb.Append((char)ReadNum(prB, i, prType, swap));
                return (name, sb.ToString());
            }
            // numérico: pr está en COLUMN-major → convertir a row-major del PyNdArray
            var outp = new double[total];
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < cols; j++)
                    outp[i * cols + j] = ReadNum(prB, j * rows + i, prType, swap);
            return (name, new PyNdArray(outp, new[] { rows, cols }));
        }

        // ======================= SAVEMAT =======================
        public static void SaveMat(string path, PyDict vars)
        {
            using var ms = new MemoryStream();
            // header 128 bytes
            var hdr = new byte[128];
            var txt = Encoding.ASCII.GetBytes("MATLAB 5.0 MAT-file, Hekatan Python3 embedded");
            Array.Copy(txt, hdr, txt.Length);
            hdr[124] = 0x00; hdr[125] = 0x01;   // version 0x0100
            hdr[126] = (byte)'I'; hdr[127] = (byte)'M';   // little-endian
            ms.Write(hdr, 0, 128);
            for (int v = 0; v < vars.Count; v++)
            {
                string name = PyOps.Str(vars.Keys[v]); object val = vars.Values[v];
                WriteMatrix(ms, name, val);
            }
            File.WriteAllBytes(path, ms.ToArray());
        }

        private static void WriteMatrix(MemoryStream outer, string name, object val)
        {
            int rows, cols; double[] data; bool isChar = false; string str = null;
            if (val is string sv) { isChar = true; str = sv; rows = 1; cols = sv.Length; data = null; }
            else { var nd = PyNumpy.AsArr(val); if (nd.Ndim == 2) { rows = nd.Rows; cols = nd.Cols; } else { rows = 1; cols = nd.Size; } data = nd.Data; }

            using var body = new MemoryStream();
            // 1) Array Flags (miUINT32, 8 bytes): [clase | flags], nzmax
            int cls = isChar ? mxCHAR : mxDOUBLE;
            WriteTag(body, miUINT32, 8); WriteI32(body, cls); WriteI32(body, 0);
            // 2) Dimensions (miINT32)
            WriteTag(body, miINT32, 8); WriteI32(body, rows); WriteI32(body, cols);
            // 3) Name (miINT8)
            var nb = Encoding.ASCII.GetBytes(name); WriteTag(body, miINT8, nb.Length); WriteBytesPad(body, nb);
            // 4) Real part
            if (isChar)
            {
                WriteTag(body, miUINT16, str.Length * 2);
                foreach (char c in str) { body.WriteByte((byte)(c & 0xFF)); body.WriteByte((byte)((c >> 8) & 0xFF)); }
                Pad(body, str.Length * 2);
            }
            else
            {
                WriteTag(body, miDOUBLE, rows * cols * 8);
                for (int j = 0; j < cols; j++) for (int i = 0; i < rows; i++)   // COLUMN-major
                    { var bb = BitConverter.GetBytes(data[i * cols + j]); body.Write(bb, 0, 8); }
            }
            var bodyArr = body.ToArray();
            WriteTag(outer, miMATRIX, bodyArr.Length); outer.Write(bodyArr, 0, bodyArr.Length);
            Pad(outer, bodyArr.Length);
        }

        // ---- helpers ----
        private static int ReadI32(byte[] b, int p, bool swap)
        {
            if (p + 4 > b.Length) return 0;
            return swap ? (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]
                        : b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
        }
        private static double ReadNum(byte[] b, int idx, int type, bool swap)
        {
            switch (type)
            {
                case miDOUBLE: { var t = Slice(b, idx * 8, 8, swap); return BitConverter.ToDouble(t, 0); }
                case miSINGLE: { var t = Slice(b, idx * 4, 4, swap); return BitConverter.ToSingle(t, 0); }
                case miINT32: case miUINT32: return ReadI32(b, idx * 4, swap);
                case miINT16: case miUINT16: { int p = idx * 2; return swap ? (short)((b[p] << 8) | b[p + 1]) : (short)(b[p] | (b[p + 1] << 8)); }
                case miINT8: case miUINT8: case miUTF8: return idx < b.Length ? b[idx] : 0;
                case miINT64: case miUINT64: { var t = Slice(b, idx * 8, 8, swap); return BitConverter.ToInt64(t, 0); }
                default: return idx < b.Length ? b[idx] : 0;
            }
        }
        private static byte[] Slice(byte[] b, int off, int len, bool swap)
        {
            var r = new byte[len]; for (int i = 0; i < len; i++) r[i] = off + i < b.Length ? b[off + i] : (byte)0;
            if (swap) Array.Reverse(r); return r;
        }
        private static byte[] Inflate(byte[] data)
        {
            using var inp = new MemoryStream(data);
            using var z = new ZLibStream(inp, CompressionMode.Decompress);
            using var outp = new MemoryStream(); z.CopyTo(outp); return outp.ToArray();
        }
        private static void WriteTag(Stream s, int dtype, int nbytes) { WriteI32(s, dtype); WriteI32(s, nbytes); }
        private static void WriteI32(Stream s, int v) { s.WriteByte((byte)(v & 0xFF)); s.WriteByte((byte)((v >> 8) & 0xFF)); s.WriteByte((byte)((v >> 16) & 0xFF)); s.WriteByte((byte)((v >> 24) & 0xFF)); }
        private static void WriteBytesPad(Stream s, byte[] b) { s.Write(b, 0, b.Length); Pad(s, b.Length); }
        private static void Pad(Stream s, int nbytes) { int r = nbytes % 8; if (r != 0) for (int i = 0; i < 8 - r; i++) s.WriteByte(0); }
    }
}

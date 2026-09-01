using System.Collections.Generic;

namespace PPack
{
    /// <summary>명령의 종류. <b>순서를 바꾸면 안 된다</b> — 값이 곧 와이어 포맷이다.</summary>
    public enum ESnowCommandKind : byte
    {
        BladeCut = 0,
        BallHarvest = 1,
        BallRelease = 2,
        BallBurst = 3,
        Gather = 4,
    }

    /// <summary>
    /// 눈에 일어난 <b>원인</b> 하나. 결과(셀 높이)가 아니라 이것을 복제한다 —
    /// `docs/specs/2026-08-21-snow-quadtree-commands.md` 2절.
    ///
    /// <para><b>모든 값이 정수다.</b> 자세를 명령이 싣고 다니므로 보간된 원격 트랜스폼을 읽지
    /// 않고, 정수라서 플랫폼을 넘어 같은 셀 집합이 나온다(실험 1). 그 둘이 2026-08-18 에 이
    /// 방향을 폐기시킨 두 원인이었다.</para>
    /// </summary>
    public struct SnowCommand
    {
        /// <summary>적용 순서. 모든 피어가 이 순서로 적용해야 한다.</summary>
        public uint Tick;

        public ESnowCommandKind Kind;

        /// <summary>누가 냈는가(차량·공의 네트워크 id). 같은 틱 안의 순서를 정하는 데도 쓴다.</summary>
        public ushort Actor;

        public int PrevXMm;
        public int PrevZMm;
        public int NowXMm;
        public int NowZMm;

        /// <summary>Q15 전방. 스윕의 자세가 여기서 나온다.</summary>
        public short FwdX;
        public short FwdZ;

        /// <summary>종류별 정수 하나 — 날 반폭(mm)·공 반지름(mm)·잔량(mm).</summary>
        public int Param;
    }

    /// <summary>
    /// 명령 목록 ↔ 바이트. <b>길이를 앞에 쓰지 않는다</b> — 명령 하나가 고정 길이라 개수는
    /// 바이트 수에서 나온다. 같은 이유로 프레이밍 버그가 생길 자리가 없다.
    /// </summary>
    public static class SnowCommandWire
    {
        /// <summary>명령 하나의 바이트. 4 + 1 + 2 + 16 + 4 + 4 = 31 → 32 로 맞춘다.</summary>
        public const int Stride = 32;

        public static byte[] Write(IReadOnlyList<SnowCommand> commands)
        {
            var bytes = new byte[commands.Count * Stride];
            int at = 0;

            for (int i = 0; i < commands.Count; i++)
            {
                SnowCommand c = commands[i];
                PutU32(bytes, ref at, c.Tick);
                bytes[at++] = (byte)c.Kind;
                PutU16(bytes, ref at, c.Actor);
                PutI32(bytes, ref at, c.PrevXMm);
                PutI32(bytes, ref at, c.PrevZMm);
                PutI32(bytes, ref at, c.NowXMm);
                PutI32(bytes, ref at, c.NowZMm);
                PutI16(bytes, ref at, c.FwdX);
                PutI16(bytes, ref at, c.FwdZ);
                PutI32(bytes, ref at, c.Param);
                at += Stride - 31;                  // 패딩
            }
            return bytes;
        }

        public static void Read(byte[] bytes, List<SnowCommand> into)
        {
            into.Clear();
            int count = bytes.Length / Stride;
            int at = 0;

            for (int i = 0; i < count; i++)
            {
                var c = new SnowCommand
                {
                    Tick = GetU32(bytes, ref at),
                };
                c.Kind = (ESnowCommandKind)bytes[at++];
                c.Actor = GetU16(bytes, ref at);
                c.PrevXMm = GetI32(bytes, ref at);
                c.PrevZMm = GetI32(bytes, ref at);
                c.NowXMm = GetI32(bytes, ref at);
                c.NowZMm = GetI32(bytes, ref at);
                c.FwdX = GetI16(bytes, ref at);
                c.FwdZ = GetI16(bytes, ref at);
                c.Param = GetI32(bytes, ref at);
                at += Stride - 31;
                into.Add(c);
            }
        }

        /// <summary>
        /// 명령 하나를 필드에 적용한다. <b>절삭만 한다</b> — 이완은 각 피어가 스스로 돌린다.
        /// </summary>
        /// <returns>걷어낸 양(mm·셀).</returns>
        public static long Apply(SnowHeightFieldCpu field, in SnowCommand c, List<int> scratch)
        {
            var prev = new SnowSweepInt.PoseI
            {
                CenterXMm = c.PrevXMm, CenterZMm = c.PrevZMm, FwdX = c.FwdX, FwdZ = c.FwdZ,
            };
            var now = new SnowSweepInt.PoseI
            {
                CenterXMm = c.NowXMm, CenterZMm = c.NowZMm, FwdX = c.FwdX, FwdZ = c.FwdZ,
            };
            var shape = new SnowSweepInt.ShapeI
            {
                HalfWidthMm = c.Param,
                HalfDepthMm = 175,
            };

            SnowSweepInt.CollectCells(field.Geo, prev, now, shape, 8, scratch);

            long cut = 0;
            foreach (int ci in scratch)
            {
                int h = field.HeightMm[ci];
                if (h <= 0) continue;
                cut += -field.AddAt(ci, -h);
            }
            return cut;
        }

        private static void PutU32(byte[] b, ref int at, uint v)
        {
            b[at++] = (byte)v; b[at++] = (byte)(v >> 8); b[at++] = (byte)(v >> 16); b[at++] = (byte)(v >> 24);
        }

        private static void PutI32(byte[] b, ref int at, int v) => PutU32(b, ref at, (uint)v);

        private static void PutU16(byte[] b, ref int at, ushort v)
        {
            b[at++] = (byte)v; b[at++] = (byte)(v >> 8);
        }

        private static void PutI16(byte[] b, ref int at, short v) => PutU16(b, ref at, (ushort)v);

        private static uint GetU32(byte[] b, ref int at)
        {
            uint v = (uint)(b[at] | (b[at + 1] << 8) | (b[at + 2] << 16) | (b[at + 3] << 24));
            at += 4;
            return v;
        }

        private static int GetI32(byte[] b, ref int at) => (int)GetU32(b, ref at);

        private static ushort GetU16(byte[] b, ref int at)
        {
            ushort v = (ushort)(b[at] | (b[at + 1] << 8));
            at += 2;
            return v;
        }

        private static short GetI16(byte[] b, ref int at) => (short)GetU16(b, ref at);
    }
}

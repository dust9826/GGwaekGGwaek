using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 눈 필드의 블록 동기화. 순수 C# 이므로 그래픽·넷코드 없이 검증된다 — 전송 계층만 빠져 있다.
    ///
    /// 지키는 성질은 넷이다: 같은 상태면 아무것도 보내지 않는다, 비용이 불일치량에 비례한다,
    /// 왕복하면 두 필드가 <b>비트 단위로</b> 같아진다, 격자가 블록 크기로 나눠지지 않아도 성립한다.
    /// </summary>
    public sealed class SnowFieldSyncTests
    {
        // 서버·클라는 같은 격자 크기를 공유한다. 이것이 페이로드에 길이를 안 쓰는 근거다.
        private static SnowField MakeField() => new SnowField(-8f, -8f, 16f, 16f, 0.125f, 30);

        // 폭·높이가 BlockCells(16)로 나눠지지 않는 격자 — 40 × 24 셀.
        private static SnowField MakeRaggedField() => new SnowField(0f, 0f, 5f, 3f, 0.125f, 30);

        private static SnowStampArea Pad(float x, float z) =>
            new SnowStampArea(x, z, 0f, 1f, 1.2f, 0.9f);

        private static int Reconcile(SnowField server, SnowField client)
        {
            var serverHashes = new uint[server.BlockCount];
            var clientHashes = new uint[client.BlockCount];
            server.WriteBlockHashes(serverHashes);
            client.WriteBlockHashes(clientHashes);

            var mismatched = new int[server.BlockCount];
            int count = server.CollectMismatchedBlocks(clientHashes, mismatched);
            if (count == 0) return 0;

            var payload = new byte[SnowField.MaxBlockPayloadBytes(count)];
            int written = server.WriteBlocks(new System.ReadOnlySpan<int>(mismatched, 0, count), payload);
            Assert.AreEqual(count, client.ReadBlocks(new System.ReadOnlySpan<byte>(payload, 0, written)),
                "보낸 블록 수와 적용한 블록 수는 같아야 한다");
            return count;
        }

        private static void AssertSameDepths(SnowField a, SnowField b)
        {
            Assert.AreEqual(a.Width, b.Width);
            Assert.AreEqual(a.Height, b.Height);
            for (int y = 0; y < a.Height; y++)
            {
                for (int x = 0; x < a.Width; x++)
                {
                    Assert.AreEqual(a.DepthCmAtCell(x, y), b.DepthCmAtCell(x, y), $"셀 ({x},{y})");
                }
            }
            Assert.AreEqual(a.TotalDepthCm, b.TotalDepthCm, "합도 정확히 같아야 한다");
        }

        [Test]
        public void 같은_상태면_보낼_블록이_없다()
        {
            var server = MakeField();
            var client = MakeField();

            Assert.AreEqual(0, Reconcile(server, client),
                "정상 상태에서 대역폭을 쓰면 해시 비교가 무의미하다");
        }

        [Test]
        public void 한_셀만_달라도_그_블록_하나만_보낸다()
        {
            var server = MakeField();
            var client = MakeField();

            server.ApplyCellDelta(20, 20, -7);   // 블록 (1,1) 하나에 들어 있다

            Assert.AreEqual(1, Reconcile(server, client),
                "비용은 격자 해상도가 아니라 불일치량에 비례해야 한다");
            AssertSameDepths(server, client);
        }

        [Test]
        public void 제설_한_번의_결과가_왕복한다()
        {
            var server = MakeField();
            var client = MakeField();

            server.ApplyStamp(10, stampId: 1, Pad(0f, 0f), -10);
            server.ApplyStamp(11, stampId: 1, Pad(1.5f, 0.5f), -30);   // 바닥까지 치운 자리

            int sent = Reconcile(server, client);

            Assert.Greater(sent, 0);
            Assert.Less(sent, server.BlockCount, "패드가 격자 전체를 덮지는 않는다");
            AssertSameDepths(server, client);
        }

        [Test]
        public void 블록_크기로_나눠지지_않는_격자도_왕복한다()
        {
            var server = MakeRaggedField();
            var client = MakeRaggedField();

            Assert.AreEqual(40, server.Width);
            Assert.AreEqual(24, server.Height);
            Assert.AreEqual(3, server.BlockCountX, "40 셀은 16 으로 나눠지지 않는다");
            Assert.AreEqual(2, server.BlockCountY);

            // 마지막 열·행의 작은 블록을 반드시 건드린다.
            server.ApplyCellDelta(39, 23, -12);
            server.ApplyCellDelta(0, 0, -5);

            Assert.AreEqual(2, Reconcile(server, client));
            AssertSameDepths(server, client);
        }

        [Test]
        public void 잘린_블록의_셀_수는_격자_경계를_따른다()
        {
            var field = MakeRaggedField();

            // 인덱스 = by * BlockCountX + bx. 오른쪽 끝 열은 40 - 32 = 8 셀 폭.
            Assert.AreEqual(16 * 16, field.BlockCellCount(0), "안쪽 블록은 꽉 찬다");
            Assert.AreEqual(8 * 16, field.BlockCellCount(2), "마지막 열은 8 셀 폭");
            Assert.AreEqual(16 * 8, field.BlockCellCount(3), "마지막 행은 8 셀 높이");
            Assert.AreEqual(8 * 8, field.BlockCellCount(5), "모서리는 둘 다 잘린다");
        }

        [Test]
        public void 대역폭_예산이_짧으면_여러_주기에_걸쳐_수렴한다()
        {
            var server = MakeField();
            var client = MakeField();

            // 여러 블록에 걸쳐 어긋나게 만든다.
            for (int i = 0; i < 6; i++) server.ApplyCellDelta(i * 20, i * 15, -9);

            var clientHashes = new uint[client.BlockCount];
            var oneAtATime = new int[1];                 // 주기당 블록 하나만 허용
            int cycles = 0;

            while (true)
            {
                client.WriteBlockHashes(clientHashes);
                int count = server.CollectMismatchedBlocks(clientHashes, oneAtATime);
                if (count == 0) break;

                Assert.AreEqual(1, count, "예산이 1이면 한 개만 담아야 한다");
                var payload = new byte[SnowField.MaxBlockPayloadBytes(1)];
                int written = server.WriteBlocks(new System.ReadOnlySpan<int>(oneAtATime, 0, 1), payload);
                client.ReadBlocks(new System.ReadOnlySpan<byte>(payload, 0, written));

                cycles++;
                Assert.Less(cycles, 64, "수렴하지 않으면 같은 블록을 무한히 다시 보내고 있다");
            }

            Assert.Greater(cycles, 1, "여러 주기를 거쳐야 하는 상황이었다");
            AssertSameDepths(server, client);
        }

        [Test]
        public void 격자_크기가_어긋난_해시는_거부한다()
        {
            var field = MakeField();
            var wrongSize = new uint[field.BlockCount - 1];

            Assert.Throws<System.ArgumentException>(() =>
                field.CollectMismatchedBlocks(wrongSize, new int[1]),
                "조용히 부분 비교하면 영원히 수렴하지 않는 상태가 된다");
        }

        [Test]
        public void 끊긴_페이로드는_거부한다()
        {
            var server = MakeField();
            var client = MakeField();
            server.ApplyCellDelta(5, 5, -11);

            var payload = new byte[SnowField.MaxBlockPayloadBytes(1)];
            int written = server.WriteBlocks(new[] { 0 }, payload);

            Assert.Throws<System.ArgumentException>(() =>
                client.ReadBlocks(new System.ReadOnlySpan<byte>(payload, 0, written - 3)),
                "블록 중간에서 끊긴 페이로드를 적용하면 깊이가 조용히 어긋난다");
        }
    }
}

using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// <see cref="SnowField"/> 는 순수 C# 이라 에디터·씬·GPU 없이 검증된다.
    /// 이 프로젝트의 첫 유닛 테스트이고, 지키는 것은 스펙 §11 의 합격 기준 2·3 이다.
    /// </summary>
    public sealed class SnowFieldTests
    {
        // 16×16m, 12.5cm 셀, 최대 30cm
        private static SnowField MakeField() => new SnowField(-8f, -8f, 16f, 16f, 0.125f, 30);

        private static SnowStampArea Pad(float x, float z) =>
            new SnowStampArea(x, z, 0f, 1f, 1.2f, 0.9f);   // 2.4 × 1.8 m — 차량 패드 크기

        [Test]
        public void 격자가_스펙대로_잡힌다()
        {
            var field = MakeField();
            Assert.AreEqual(128, field.Width);
            Assert.AreEqual(128, field.Height);
            Assert.AreEqual(30, field.DepthCmAtWorld(0f, 0f), "시작은 눈 가득이다");
        }

        [Test]
        public void 같은_스탬프를_여러_번_적용해도_한_번과_같다()
        {
            var once = MakeField();
            var many = MakeField();

            int removedOnce = once.ApplyStamp(10, stampId: 1, Pad(0f, 0f), -10);

            int removedFirst = many.ApplyStamp(10, stampId: 1, Pad(0f, 0f), -10);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(0, many.ApplyStamp(10, stampId: 1, Pad(0f, 0f), -10),
                    "재적용은 아무것도 제거하지 않아야 한다");
            }

            Assert.AreEqual(removedOnce, removedFirst);
            AssertSameDepths(once, many);
        }

        [Test]
        public void 다른_틱이면_다시_적용된다()
        {
            var field = MakeField();
            field.ApplyStamp(10, stampId: 1, Pad(0f, 0f), -10);
            Assert.AreEqual(20, field.DepthCmAtWorld(0f, 0f));

            field.ApplyStamp(11, stampId: 1, Pad(0f, 0f), -10);
            Assert.AreEqual(10, field.DepthCmAtWorld(0f, 0f), "틱이 다르면 또 밀린다");
        }

        [Test]
        public void 같은_틱에_겹친_두_도구는_둘_다_적용된다()
        {
            var field = MakeField();
            field.ApplyStamp(10, stampId: 1, Pad(0f, 0f), -10);
            field.ApplyStamp(10, stampId: 2, Pad(0f, 0f), -10);

            Assert.AreEqual(10, field.DepthCmAtWorld(0f, 0f),
                "셀 단위 가드였다면 한쪽이 조용히 버려졌을 것이다");
        }

        [Test]
        public void 스탬프_순서가_결과를_바꾸지_않는다_포화_전에는()
        {
            var forward = MakeField();
            forward.ApplyStamp(10, 1, Pad(0f, 0f), -10);
            forward.ApplyStamp(10, 2, Pad(0.3f, 0.2f), -5);

            var backward = MakeField();
            backward.ApplyStamp(10, 2, Pad(0.3f, 0.2f), -5);
            backward.ApplyStamp(10, 1, Pad(0f, 0f), -10);

            AssertSameDepths(forward, backward);
        }

        [Test]
        public void 쌓기와_밀기는_같은_연산이다()
        {
            var field = MakeField();
            field.ApplyStamp(10, 1, Pad(0f, 0f), -30);
            Assert.AreEqual(0, field.DepthCmAtWorld(0f, 0f));

            field.ApplyStamp(11, 2, Pad(0f, 0f), +20);   // 이벤트·몬스터 적설
            Assert.AreEqual(20, field.DepthCmAtWorld(0f, 0f));
        }

        [Test]
        public void 최대_깊이와_0_에서_포화된다()
        {
            var field = MakeField();
            field.ApplyStamp(1, 1, Pad(0f, 0f), +100);
            Assert.AreEqual(30, field.DepthCmAtWorld(0f, 0f), "최대 깊이를 넘지 않는다");

            field.ApplyStamp(2, 1, Pad(0f, 0f), -100);
            Assert.AreEqual(0, field.DepthCmAtWorld(0f, 0f), "음수로 내려가지 않는다");
        }

        [Test]
        public void 패드_밖은_건드리지_않는다()
        {
            var field = MakeField();
            field.ApplyStamp(10, 1, Pad(0f, 0f), -30);

            Assert.AreEqual(0, field.DepthCmAtWorld(0f, 0f), "패드 안");
            Assert.AreEqual(30, field.DepthCmAtWorld(3f, 0f), "패드 밖 — 좌우 반폭 0.9m 를 넘는다");
            Assert.AreEqual(30, field.DepthCmAtWorld(0f, 4f), "패드 밖 — 진행 방향 반길이 1.2m 를 넘는다");
        }

        [Test]
        public void 회전한_패드는_회전한_사각형을_지운다()
        {
            var field = MakeField();
            // 45° 로 돌린 패드
            var rotated = new SnowStampArea(0f, 0f, 1f, 1f, 1.2f, 0.9f);
            field.ApplyStamp(10, 1, rotated, -30);

            Assert.AreEqual(0, field.DepthCmAtWorld(0.7f, 0.7f), "긴 축 방향(대각선)은 지워진다");
            Assert.AreEqual(30, field.DepthCmAtWorld(-1.1f, 1.1f), "짧은 축 방향은 남는다");
        }

        [Test]
        public void 더티_rect_는_건드린_셀만_감싼다()
        {
            var field = MakeField();
            field.ClearDirty();
            field.ApplyStamp(10, 1, Pad(0f, 0f), -10);

            var (x, y, w, h) = field.DirtyRect;
            Assert.Greater(w, 0);
            Assert.Greater(h, 0);
            // 2.4 × 1.8 m 를 12.5cm 셀로 = 약 19 × 14 칸. 격자 전체(128)와는 자릿수가 다르다.
            Assert.LessOrEqual(w, 24, "패드보다 크게 부풀지 않는다");
            Assert.LessOrEqual(h, 24);
            Assert.GreaterOrEqual(x, 0);
            Assert.GreaterOrEqual(y, 0);
        }

        [Test]
        public void 아무것도_안_바뀌면_제거량이_0_이다()
        {
            var field = MakeField();
            field.ApplyStamp(10, 1, Pad(0f, 0f), -30);

            int removed = field.ApplyStamp(11, 2, Pad(0f, 0f), -30);
            Assert.AreEqual(0, removed, "이미 치워진 자리는 연출도 뜨지 않아야 한다");
        }

        [Test]
        public void 블록_해시가_바뀐_블록만_가리킨다()
        {
            var field = MakeField();
            uint before = field.BlockHash(0, 0);
            uint farBefore = field.BlockHash(field.BlockCountX - 1, field.BlockCountY - 1);

            field.ApplyStamp(10, 1, new SnowStampArea(-7.5f, -7.5f, 0f, 1f, 0.3f, 0.3f), -10);

            Assert.AreNotEqual(before, field.BlockHash(0, 0), "건드린 블록의 해시는 바뀐다");
            Assert.AreEqual(farBefore, field.BlockHash(field.BlockCountX - 1, field.BlockCountY - 1),
                "먼 블록은 그대로다 — 동기화 비용이 불일치량에 비례하는 근거");
        }

        [Test]
        public void 블록_직렬화가_왕복한다()
        {
            var source = MakeField();
            source.ApplyStamp(10, 1, Pad(-7f, -7f), -20);

            var destination = MakeField();
            var buffer = new byte[SnowField.BlockCells * SnowField.BlockCells];
            int written = source.WriteBlock(0, 0, buffer);
            destination.ReadBlock(0, 0, new System.ReadOnlySpan<byte>(buffer, 0, written));

            for (int y = 0; y < SnowField.BlockCells; y++)
            for (int x = 0; x < SnowField.BlockCells; x++)
            {
                Assert.AreEqual(source.DepthCmAtCell(x, y), destination.DepthCmAtCell(x, y));
            }
        }

        [Test]
        public void 스냅샷이_왕복한다()
        {
            var source = MakeField();
            source.ApplyStamp(10, 1, Pad(0f, 0f), -25);

            var destination = MakeField();
            destination.LoadSnapshot(source.Snapshot());
            AssertSameDepths(source, destination);
        }

        private static void AssertSameDepths(SnowField a, SnowField b)
        {
            Assert.AreEqual(a.Width, b.Width);
            Assert.AreEqual(a.Height, b.Height);
            for (int y = 0; y < a.Height; y++)
            for (int x = 0; x < a.Width; x++)
            {
                if (a.DepthCmAtCell(x, y) != b.DepthCmAtCell(x, y))
                {
                    Assert.Fail($"셀 ({x},{y}) 이 다르다: {a.DepthCmAtCell(x, y)} vs {b.DepthCmAtCell(x, y)}");
                }
            }
        }
    }
}

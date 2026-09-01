using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 순서와 시간만 본다. 그리는 것과 세션 배선은 여기서 덮지 않는다 — <c>UIDocument</c> 는 패널
    /// 없이는 트리를 만들지 않고, 실제 피어가 나가는 것은 인스턴스 둘이 필요하다.
    /// </summary>
    public sealed class WorldMessageQueueTests
    {
        private static WorldMessageQueue New() => new WorldMessageQueue(3f, 0.25f);

        [Test]
        public void 넣기_전에는_보여줄_것이_없다()
        {
            WorldMessageQueue queue = New();
            queue.Tick(0f);

            Assert.That(queue.Current, Is.Null);
            Assert.That(queue.Opacity(0f), Is.EqualTo(0f));
        }

        [Test]
        public void 넣으면_다음_틱에_뜬다()
        {
            WorldMessageQueue queue = New();
            queue.Enqueue("PLAYER 2 LEFT");
            queue.Tick(10f);

            Assert.That(queue.Current, Is.EqualTo("PLAYER 2 LEFT"));
        }

        [Test]
        public void 시간이_지나면_사라진다()
        {
            WorldMessageQueue queue = New();
            queue.Enqueue("A");
            queue.Tick(0f);
            queue.Tick(3f);

            Assert.That(queue.Current, Is.Null, "3초가 지나면 내려가야 한다");
        }

        [Test]
        public void 겹치면_하나씩_줄을_선다()
        {
            WorldMessageQueue queue = New();
            queue.Enqueue("A");
            queue.Enqueue("B");

            queue.Tick(0f);
            Assert.That(queue.Current, Is.EqualTo("A"), "두 줄이 동시에 뜨면 어느 것이 방금 일인지 알 수 없다");

            queue.Tick(3f);
            Assert.That(queue.Current, Is.EqualTo("B"));
        }

        [Test]
        public void 빈_문구는_무시한다()
        {
            WorldMessageQueue queue = New();
            queue.Enqueue(null);
            queue.Enqueue("");
            queue.Enqueue("   ");
            queue.Tick(0f);

            Assert.That(queue.Current, Is.Null);
        }

        [Test]
        public void 들어올_때와_나갈_때_페이드한다()
        {
            WorldMessageQueue queue = New();
            queue.Enqueue("A");
            queue.Tick(0f);

            Assert.That(queue.Opacity(0f), Is.EqualTo(0f), "시작은 투명하다");
            Assert.That(queue.Opacity(0.25f), Is.EqualTo(1f), "페이드인이 끝나면 불투명하다");
            Assert.That(queue.Opacity(1.5f), Is.EqualTo(1f), "중간은 그대로 보인다");
            Assert.That(queue.Opacity(3f), Is.EqualTo(0f), "끝은 다시 투명하다");
        }

        [Test]
        public void 비우면_줄_선_것까지_사라진다()
        {
            WorldMessageQueue queue = New();
            queue.Enqueue("A");
            queue.Enqueue("B");
            queue.Tick(0f);

            queue.Clear();
            queue.Tick(1f);

            Assert.That(queue.Current, Is.Null, "씬이 바뀌면 지난 판의 메시지가 따라오면 안 된다");
        }
    }
}

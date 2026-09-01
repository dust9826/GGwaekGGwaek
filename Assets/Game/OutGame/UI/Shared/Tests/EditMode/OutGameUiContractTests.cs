using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// 로비 UI 의 계약을 지킨다 — <b>코드가 이름으로 찾는 요소가 UXML 에 실제로 있는가.</b>
    ///
    /// <para>없으면 <c>Q&lt;T&gt;</c> 가 <c>null</c> 을 주고, 버튼이 아무 일도 하지 않거나 라벨이 조용히
    /// 비어 있는 것으로 끝난다. 콘솔에 아무것도 남지 않아 UI 를 눈으로 볼 때까지 모른다 — 그래서
    /// 사람이 창을 띄워야만 잡히던 부류의 결함이고, 여기서 배치모드로 잡는다.</para>
    ///
    /// <para>이름 목록을 테스트에 손으로 복제하지 않고 <b>컨트롤러 소스에서 뽑는다.</b> 복제하면 그 목록이
    /// 다시 낡고, 낡은 목록은 아무것도 지키지 않으면서 초록으로 남는다. 대상은 호출 지점이 분명한 셋 —
    /// <c>Q&lt;T&gt;("name")</c>, <c>ReadTextField("name")</c>, <c>SetLabel("name"</c> 다.</para>
    ///
    /// <para>UXML 을 팀원이 고치고 코드를 내가 고치는 파일이 갈려 있어서, 머지 직후가 이 어긋남이 가장
    /// 잘 생기는 순간이다. 실제로 `/main` 의 UXML 에는 한때 `action-single-start` 가 없었다.</para>
    /// </summary>
    public sealed class OutGameUiContractTests
    {
        private const string ControllerPath =
            "Assets/Game/OutGame/UI/Shared/Scripts/OutGameScreenController.cs";

        private const string MainMenuUxmlPath =
            "Assets/Game/OutGame/UI/MainMenu/MainMenu.uxml";

        /// <summary>
        /// 싱글플레이 화면(<c>Singleplayer.uxml</c>)은 2026-08-18 기준 <b>메인 문서에 붙어 있지 않다</b> —
        /// <c>MainMenu.uxml</c> 은 그 폴더에서 스타일만 가져오고, 그 문서를 로드하는 코드가 없다. 그래서
        /// 이 이름들은 지금 어느 살아있는 문서에도 없고, 그것은 이 계약 테스트가 잡을 문제가 아니라
        /// 그 화면 작업의 남은 일이다(컨트롤러는 <c>single-nickname-input</c> 을 읽는데 그 문서의 필드는
        /// <c>nickname-input</c> 이다 — 닉네임이 항상 기본값이 된다).
        ///
        /// <para>화면이 실제로 붙는 순간 <see cref="SinglePlayerScreenRoot"/> 가 문서에 나타나고 아래
        /// 단정이 <b>이 예외를 지우라고 실패한다</b>. 예외가 조용히 영구화되지 않게 하는 장치다.</para>
        /// </summary>
        private static readonly string[] UnwiredSinglePlayerNames =
        {
            "action-single-start",
            "single-nickname-input",
        };

        private const string SinglePlayerScreenRoot = "singleplayer";

        [Test]
        public void 컨트롤러가_이름으로_찾는_요소는_MainMenu_UXML_에_모두_있다()
        {
            string source = File.ReadAllText(ControllerPath);

            var names = new SortedSet<string>();
            foreach (Match match in Regex.Matches(source, @"Q<[A-Za-z]+>\(""([a-z0-9-]+)""\)"))
            {
                names.Add(match.Groups[1].Value);
            }

            foreach (Match match in Regex.Matches(source,
                                                  @"(?:ReadTextField|SetLabel)\(""([a-z0-9-]+)"""))
            {
                names.Add(match.Groups[1].Value);
            }

            // 버튼은 반대 방향이다 — UXML 이 이름을 주고 <c>switch</c> 가 그것을 기다린다. 없으면 예외가
            // 아니라 <b>눌릴 수 없는 기능</b>이 되고, 그것이 `/main` 의 UXML 에서 `action-single-start` 가
            // 빠져 있던 모양이다. 그래서 케이스 이름도 같이 요구한다.
            foreach (Match match in Regex.Matches(source, @"case ""(action-[a-z0-9-]+)"":"))
            {
                names.Add(match.Groups[1].Value);
            }

            // 추출이 실패해도 통과하는 길을 막는다 — 정규식이 안 맞으면 빈 집합이 전부 통과한다.
            Assert.That(names.Count, Is.GreaterThan(4),
                        "컨트롤러에서 요소 이름을 못 뽑았다 - 호출 형태가 바뀌었으면 정규식을 고쳐라");

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(MainMenuUxmlPath);
            Assert.That(tree, Is.Not.Null, $"UXML 을 찾지 못했다: {MainMenuUxmlPath}");

            VisualElement root = tree.Instantiate();

            Assert.That(root.Q<VisualElement>(SinglePlayerScreenRoot), Is.Null,
                        $"싱글플레이 화면('{SinglePlayerScreenRoot}')이 메인 문서에 붙었다 - "
                        + $"{nameof(UnwiredSinglePlayerNames)} 예외를 지우고 이름을 다시 맞춰라");

            var missing = new List<string>();
            var elsewhere = new List<string>();
            foreach (string name in names)
            {
                if (root.Q<VisualElement>(name) != null) continue;
                if (System.Array.IndexOf(UnwiredSinglePlayerNames, name) >= 0) continue;
                if (DeclaredInAnotherDocument(name)) elsewhere.Add(name);
                else missing.Add(name);
            }

            Assert.That(missing, Is.Empty,
                        "코드가 찾는 이름이 어느 UXML 에도 없다(오타이거나 지워진 요소): "
                        + string.Join(", ", missing));

            // 다른 문서에만 있는 이름은 오타가 아니라 <b>문서 배선 공백</b>이다 — 원인도 담당도 다르므로
            // 실패로 만들지 않고 기록만 남긴다. 어디에도 없는 이름은 위에서 실패한다.
            if (elsewhere.Count > 0)
            {
                TestContext.WriteLine("다른 문서에만 있어 지금은 닿지 않는 이름: "
                                      + string.Join(", ", elsewhere));
            }
        }

        /// <summary>
        /// 이름이 <c>OutGame/UI</c> 아래 다른 UXML 에 선언돼 있는지 본다. 오타(어디에도 없음)와
        /// 문서 배선 공백(다른 문서에 있음)을 가른다 — 둘은 원인도 담당도 다르다.
        /// </summary>
        private static bool DeclaredInAnotherDocument(string name)
        {
            foreach (string path in Directory.GetFiles("Assets/Game/OutGame/UI", "*.uxml",
                                                       SearchOption.AllDirectories))
            {
                if (File.ReadAllText(path).Contains($"\"{name}\"")) return true;
            }

            return false;
        }
    }
}

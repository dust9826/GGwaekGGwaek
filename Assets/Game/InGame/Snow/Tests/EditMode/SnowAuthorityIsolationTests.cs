using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 스펙 §3·§5 의 구조 규약을 <b>테스트로 굳힌다.</b>
    ///
    /// 문서에만 있는 규칙은 조용히 깨진다. 눈에서 가장 비싸게 깨질 규칙이 둘이고,
    /// 둘 다 IL 을 보면 확인된다:
    /// <list type="number">
    /// <item><b>권위는 그래픽 타입을 모른다</b> — 데디 서버에는 GPU 가 없다</item>
    /// <item><b>판정은 텍스처를 읽지 않는다</b> — 텍스처는 권위의 파생물일 뿐이다</item>
    /// </list>
    ///
    /// 진짜 헤드리스 실행(<c>-batchmode -nographics</c>)을 대체하지는 않는다. 그것은
    /// 환경을 보고, 이것은 <b>참조 그래프</b>를 본다 — 후자는 CI 없이도 매번 돈다.
    /// </summary>
    public sealed class SnowAuthorityIsolationTests
    {
        // 권위 쪽이 만져서는 안 되는 타입들. 이름으로 막는다 — 어셈블리 참조를 끊는 것은
        // MonoBehaviour 를 쓰는 컴포넌트에서는 불가능하기 때문이다.
        private static readonly string[] ForbiddenTypeNames =
        {
            "UnityEngine.Texture", "UnityEngine.Texture2D", "UnityEngine.RenderTexture",
            "UnityEngine.Shader", "UnityEngine.Material", "UnityEngine.Graphics",
            "UnityEngine.ComputeShader", "UnityEngine.Mesh",
        };

        [Test]
        public void SnowField_는_UnityEngine_타입을_전혀_쓰지_않는다()
        {
            Type type = typeof(SnowField);
            Assert.AreEqual("PPack", type.Namespace);

            var offenders = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                            BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(SignatureTypes)
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("UnityEngine"))
                .Select(t => t.FullName)
                .Distinct()
                .ToArray();

            Assert.IsEmpty(offenders,
                "SnowField 는 순수 C# 이어야 한다(헤드리스). 발견: " + string.Join(", ", offenders));
        }

        [Test]
        public void SnowStampArea_도_순수_C_샤프다()
        {
            var offenders = typeof(SnowStampArea)
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(SignatureTypes)
                .Where(t => t.Namespace != null && t.Namespace.StartsWith("UnityEngine"))
                .Select(t => t.FullName)
                .Distinct()
                .ToArray();

            Assert.IsEmpty(offenders, "발견: " + string.Join(", ", offenders));
        }

        [TestCase(typeof(SnowStage))]
        [TestCase(typeof(SnowVehicleDrag))]
        [TestCase(typeof(SnowVehiclePad))]
        public void 권위와_판정은_그래픽_타입을_만지지_않는다(Type type)
        {
            var offenders = type
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(SignatureTypes)
                .Where(t => t.FullName != null && ForbiddenTypeNames.Contains(t.FullName))
                .Select(t => t.FullName)
                .Distinct()
                .ToArray();

            Assert.IsEmpty(offenders,
                $"{type.Name} 은 텍스처·머티리얼을 몰라야 한다. 발견: " + string.Join(", ", offenders));
        }

        [Test]
        public void 렌더러만_그래픽을_안다()
        {
            // 반대 방향 확인 — 규칙이 "아무도 안 만진다"로 잘못 굳는 것을 막는다.
            bool touchesTexture = typeof(SnowSurfaceRenderer)
                .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                            BindingFlags.Instance | BindingFlags.Static)
                .SelectMany(SignatureTypes)
                .Any(t => t.FullName == "UnityEngine.Texture2D");

            Assert.IsTrue(touchesTexture,
                "연출 쪽은 텍스처를 알아야 한다 — 그렇지 않으면 업로드가 사라진 것이다");
        }

        private static System.Collections.Generic.IEnumerable<Type> SignatureTypes(MemberInfo member)
        {
            switch (member)
            {
                case FieldInfo f:
                    yield return f.FieldType;
                    break;
                case PropertyInfo p:
                    yield return p.PropertyType;
                    break;
                case MethodInfo m:
                    yield return m.ReturnType;
                    foreach (var parameter in m.GetParameters()) yield return parameter.ParameterType;
                    break;
                case ConstructorInfo c:
                    foreach (var parameter in c.GetParameters()) yield return parameter.ParameterType;
                    break;
            }
        }
    }
}

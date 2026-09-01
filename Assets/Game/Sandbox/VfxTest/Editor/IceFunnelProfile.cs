using UnityEditor;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 얼음 소용돌이 껍데기의 실루엣. 값을 바꾸고 인스펙터의 <b>Rebuild Mesh</b> 를 누르면
    /// <see cref="_meshPath"/> 의 메시 에셋 내용이 그 자리에서 갈린다.
    ///
    /// <para><b>메시를 지우고 다시 만들지 않는다.</b> `DeleteAsset` + `CreateAsset` 는 새 GUID 를
    /// 주므로 씬의 <c>MeshFilter</c> 참조가 전부 끊긴다. 기존 에셋을 열어 <c>Clear()</c> 후
    /// 다시 채우면 GUID 가 유지된다.</para>
    ///
    /// <para>Blender 를 쓰지 않는 이유는 실루엣이 파라미터이기 때문이다 — 여기서 값을 바꾸는 것이
    /// FBX 를 다시 굽고 임포트하는 것보다 빠르고, FBX 루트의 100 배 스케일 / -90도 회전 함정이
    /// 아예 생기지 않는다.</para>
    /// </summary>
    internal sealed class IceFunnelProfile : ScriptableObject
    {
        [Tooltip("꼭짓점(바닥) 반지름. 0 이면 한 점으로 모인다.")]
        [SerializeField, Min(0f)] private float _baseRadius = 0.08f;

        [Tooltip("입구(위) 반지름. 이 값이 원뿔이 얼마나 벌어지는지를 정한다.")]
        [SerializeField, Min(0.01f)] private float _topRadius = 3.1f;

        [SerializeField, Min(0.01f)] private float _height = 7f;

        [Tooltip("r(t) = base + (top-base) * t^curve. 1 보다 크면 아래가 좁게 유지되다가 " +
                 "위에서 벌어진다(깔때기). 1 미만이면 아래가 뚱뚱해져 그냥 원뿔이 된다.")]
        [SerializeField, Range(0.2f, 5f)] private float _curve = 1.9f;

        [Tooltip("세로 링 수. 정점 변위를 쓰므로 적으면 면이 각져 보인다.")]
        [SerializeField, Range(8, 256)] private int _rings = 80;

        [Tooltip("둘레 분할 수.")]
        [SerializeField, Range(8, 256)] private int _segments = 96;

        [SerializeField] private string _meshPath = "Assets/Game/Sandbox/VfxTest/Meshes/SM_IceFunnel.asset";

        public float BaseRadius => _baseRadius;
        public float TopRadius => _topRadius;
        public float Height => _height;
        public float Curve => _curve;
        public int Rings => _rings;
        public int Segments => _segments;
        public string MeshPath => _meshPath;

        /// <summary>지름과 벌어진 각도. 값을 고를 때 눈으로 확인하라고 인스펙터에 띄운다.</summary>
        public string Describe()
        {
            float halfAngle = Mathf.Atan2(_topRadius - _baseRadius, _height) * Mathf.Rad2Deg;
            float mid = _baseRadius + (_topRadius - _baseRadius) * Mathf.Pow(0.5f, _curve);
            return $"입구 지름 {_topRadius * 2f:0.00} m · 높이 {_height:0.00} m · 중간 반지름 {mid:0.00} m · " +
                   $"벽 기울기 {halfAngle:0.0}°";
        }
    }
}

// ---------------------------------------------------------------------------
// AnyTest 스파이크 v7 에서 이식. 원본: Assets/SnowGrainFakeV7/Shaders/SnowLumpBakeV7.hlsl
//
// 옮긴 것은 **렌더러뿐**이다. v7 의 GPU 시뮬(SnowPileFieldV7.compute 의 Push/Settle/Deposit/
// Heap*/Relax)은 가져오지 않는다 — 권위는 CPU 의 SnowField 에 있고, 데디 서버에는 GPU 가 없다
// (루트 AGENTS.md). 이 파일이 읽는 높이 텍스처는 그 CPU 격자의 업로드 결과다.
//
// 버전 접미사(V7)와 _Cs7 전역 접두사는 이 저장소 규칙에 맞춰 제거했다(AGENTS.md 네이밍).
// 그 외의 수식·상수·주석은 원본 그대로다 — 검증된 코드를 다시 유도하지 않는다.
// ---------------------------------------------------------------------------
#ifndef SNOW_LUMP_BAKE_V7_INCLUDED
#define SNOW_LUMP_BAKE_V7_INCLUDED

// CARRIED OVER FROM v6 UNCHANGED IN SUBSTANCE. Only identifiers were renamed V6 ->  (and the
// _Cs6 shader-global prefix -> _Snow). Prose below that says "v6", "v5", "v4" or "v3" is the
// LINEAGE talking, not a claim about v7: v7 is v6 with the ball replaced by an accumulating pile
// written into the height field, and this file is one of the parts that did not have to change.

// -----------------------------------------------------------------------------------------------
// THE LUMP LATTICE, AS ONE DEFINITION, AND ITS 8-BIT ENCODING.
//
// WHY THIS FILE EXISTS. The lattice used to be evaluated inside SurfaceYFromDil, which means it ran
// once per march step, once per normal tap and once per self-shadow tap - nine hashes and a sqrt
// every time. Measured on the 42 s course: with the term off the marcher was 6.16 ms of GPU at 23.7
// mean steps; with it on, 12.7-16.5 ms at 30.6-36.2 mean steps. The look was right and the cost was
// not, and no single sub-term dominated (soft shadows 8 -> 0 bought 2.4 ms, halving the fade
// distance bought 0.6 ms), because the cost is simply the term's ARITY: ~36 evaluations per pixel.
//
// So the lift is now BAKED into a texture once per simulation step, over the dirty window, and the
// marcher takes ONE bilinear tap of it. The formula lives here, in a file included by BOTH the bake
// kernel (SnowPileField.compute) and the marcher (SnowMarchCore.hlsl), so the thing that writes
// the texture and the thing that decodes it cannot disagree about what a texel means.
//
// WHAT IS IN HERE: the lattice knobs, the hash, the 3x3 cap max, the gates, and the encode/decode
// pair. Nothing else - in particular NO texture declarations and no field geometry, because the two
// includers have completely different names for those (_HeightTex/_InvPatchSize in the marcher,
// _SrcTex/_TexelSize in the compute) and a shared declaration would collide.
//
// THE ENCODING. lift / _LumpRadiusM, stored in a single-channel 8-bit UNORM. The lift's range is
// exactly [0, _LumpRadiusM] - see SnowLumpLiftM - so the whole 0..1 code range is used whatever the
// radius is, and the quantum is _LumpRadiusM / 255: 1.18 mm at the shipped 0.30 m radius. That is
// under a hundredth of a bake texel's 6.25 cm width, so the quantisation is invisible next to the
// spatial resolution the bake already imposes.
//
// Every non-void function here has exactly ONE return: the project convention for this variant. See
// the METAL note in SnowCasualStyle.hlsl for the real constraint and why the convention is
// stricter than it.
// -----------------------------------------------------------------------------------------------

// _LumpRadiusM is THREE things at once and all three are load bearing:
//   * the sphere radius r in metres, which the bake uses;
//   * the DECODE SCALE, which the marcher multiplies the 0..1 texel by;
//   * the OFF SWITCH. It is a uniform, so `_LumpRadiusM > 1e-5` is a scalar compare across the whole
//     draw, and 0 really does mean the marcher never issues the tap - not "taps and multiplies by
//     zero". That is the A/B against the recorded 6.16 ms / 23.7 mean-step baseline.
float  _LumpRadiusM;

float  _LumpSpacingM;     // lattice spacing in metres
float  _LumpSpacingInv;   // 1 / _LumpSpacingM
float  _LumpJitter;       // 0..1 of a cell: how far a centre may wander from its own cell centre
float  _LumpRadiusVary;   // 0..1, per-lump radius reduction. Only ever REDUCES r, so the bound holds.
float  _LumpGateInv;      // 1 / the snow depth above the bare-ground threshold that fades the lift in
float  _LumpReliefInv;    // 1 / the local relief (dilY - h) that saturates the slope term
float  _LumpSlopeStrength;// 0 = lobes everywhere the snow is deep, 1 = lobes only where there is relief

// THREE decorrelated values from ONE integer hash, sliced out of its bits. Deliberately not three
// hash calls: even in a bake, nine cells x three hashes would be 27 hash evaluations per texel.
// Ten bits per field is 1/1024 of a cell of jitter precision, far finer than a 6.25 cm texel can
// express. No sin(): the sin trick loses precision unevenly and this has to be bit-stable so that a
// re-bake of a window agrees with the bake of the frame before it along the seam.
float3 SnowLumpHash3(int2 ci)
{
    uint2 u = (uint2)(ci + 4096);
    uint  h = u.x * 1597334677u ^ u.y * 3812015801u;
    h ^= h >> 15;
    h *= 2246822519u;
    h ^= h >> 13;
    h *= 3266489917u;
    h ^= h >> 16;
    return float3(uint3(h, h >> 10, h >> 20) & 1023u) * (1.0 / 1024.0);
}

// The lattice's height contribution at (x,z), in metres, BEFORE any gate.
//
//   cap_i(x,z) = (d_i < r_i) ? sqrt(r_i^2 - d_i^2) : 0     d_i = |xz - centre_i| horizontally
//   lift       = max over the 3x3 neighbourhood of cap_i
//
// EXACT UPPER BOUND: r_i = r * (1 - vary * hash) <= r for vary in 0..1, and cap_i <= r_i, so the
// result is in [0, _LumpRadiusM], with the top attainable only exactly over an unvaried centre.
// Nothing here can exceed it - there is no clamp holding the bound up - which is what makes
// lift / _LumpRadiusM a full-range 0..1 encoding rather than a lossy one.
//
// ONE sqrt, not nine: max_i sqrt(max(0, r_i^2 - d_i^2)) == sqrt(max_i max(0, r_i^2 - d_i^2)) because
// sqrt is monotone on the non-negative reals, so the neighbourhood reduces in the SQUARED domain and
// the root is taken once at the end.
//
// 3x3 IS ENOUGH, and the C# side is what keeps it enough: a cell's centre sits within
// 0.5 * (1 + jitter) * spacing of the query point's own cell centre, so a lump can only reach a
// point in a cell it is not adjacent to once r > spacing * (1.5 - 0.5 * jitter). The renderer clamps
// the radius to exactly that, so widening the neighbourhood is never needed and a lobe can never pop
// in and out as the query point crosses a cell boundary.
float SnowLumpLiftM(float2 xz)
{
    float2 g  = xz * _LumpSpacingInv;
    float2 gi = floor(g);

    float best2 = 0.0;

    [unroll]
    for (int j = -1; j <= 1; ++j)
    {
        [unroll]
        for (int i = -1; i <= 1; ++i)
        {
            float2 cell = gi + float2((float)i, (float)j);
            float3 hh   = SnowLumpHash3(int2(cell));

            // Centre in LATTICE units - cell corner plus half a cell plus the jitter - then the offset
            // back to metres, so the distance test is metric and _LumpRadiusM is a true radius.
            float2 c = cell + 0.5 + (hh.xy - 0.5) * _LumpJitter;
            float2 d = (g - c) * _LumpSpacingM;

            float ri = _LumpRadiusM * (1.0 - _LumpRadiusVary * hh.z);

            best2 = max(best2, ri * ri - dot(d, d));
        }
    }

    return sqrt(max(0.0, best2));
}

// THE GATES, in 0..1, and both of them are baked so the marcher pays for neither.
//
// depthGate is exactly 0 at and below the bare-ground threshold, so a lobe cannot float over ground
// the simulation has carved bare - which in this variant happens routinely, so this is correctness
// and not polish.
//
// slopeTerm reads the LOCAL RELIEF out of the fillet dilation: dilY is the field max over the fillet
// radius, so (dilY - h) is zero on flat virgin snow and large on a berm flank or a dumped mound.
// Flat snow therefore stays smooth while worked material gets the lobes.
//
// minSnowH is a parameter rather than a uniform because the two includers name it differently: the
// marcher calls it _MinSnowHeight and the field compute has no such uniform of its own.
float SnowLumpGate(float h, float dilY, float minSnowH)
{
    float relief    = max(0.0, dilY - h);
    float slopeTerm = lerp(1.0, saturate(relief * _LumpReliefInv), _LumpSlopeStrength);
    float depthGate = saturate((h - minSnowH) * _LumpGateInv);
    return depthGate * slopeTerm;
}

// Metres -> the 0..1 stored code. saturate() is belt and braces: SnowLumpLiftM * a gate in 0..1 is
// already inside [0, _LumpRadiusM], and an 8-bit UNORM store would clamp anyway, but writing it here
// makes the range the ENCODER's stated contract rather than a property of the storage format.
float SnowLumpEncode(float liftM)
{
    return saturate(liftM / max(1e-5, _LumpRadiusM));
}

// The 0..1 stored code -> metres. The exact inverse of the line above, which is the whole reason the
// two live in one file: change the encoding and both ends move together.
float SnowLumpDecodeM(float code)
{
    return code * _LumpRadiusM;
}

#endif // SNOW_LUMP_BAKE_V7_INCLUDED

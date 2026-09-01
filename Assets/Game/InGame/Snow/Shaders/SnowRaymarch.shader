// ---------------------------------------------------------------------------
// AnyTest 스파이크 v7 에서 이식. 원본: Assets/SnowGrainFakeV7/Shaders/SnowRaymarchV7.shader
//
// 옮긴 것은 **렌더러뿐**이다. v7 의 GPU 시뮬(SnowPileFieldV7.compute 의 Push/Settle/Deposit/
// Heap*/Relax)은 가져오지 않는다 — 권위는 CPU 의 SnowField 에 있고, 데디 서버에는 GPU 가 없다
// (루트 AGENTS.md). 이 파일이 읽는 높이 텍스처는 그 CPU 격자의 업로드 결과다.
//
// 버전 접미사(V7)와 _Cs7 전역 접두사는 이 저장소 규칙에 맞춰 제거했다(AGENTS.md 네이밍).
// 그 외의 수식·상수·주석은 원본 그대로다 — 검증된 코드를 다시 유도하지 않는다.
// ---------------------------------------------------------------------------
// CARRIED OVER FROM v6 UNCHANGED IN SUBSTANCE. Only identifiers were renamed V6 ->  (and the
// _Cs6 shader-global prefix -> _Snow). Prose below that says "v6", "v5", "v4" or "v3" is the
// LINEAGE talking, not a claim about v7: v7 is v6 with the ball replaced by an accumulating pile
// written into the height field, and this file is one of the parts that did not have to change.
// Variant B v6 - RAYMARCHED CASUAL SNOW. One proxy box, no lump instances at all. This is v6's
// DEFAULT mode: it is the cheapest of the three in the parent variant (4.95 ms against 5.41 for the
// screen-space chain), and it is the one whose surface is a function rather than a heap of primitives,
// which is what makes the casual "fat edges" treatment a two-line change to the surface definition
// instead of a different mesh.
//
// WHY A RAYMARCH AT ALL
// --------------------
// The real project's snow spec rejected parallax / POM for two specific reasons: the silhouette does not
// change, and the intersection with the vehicle is wrong. Both are consequences of parallax faking the
// surface INSIDE a fragment while still reporting the polygon's depth. A ray march that writes SV_Depth
// has neither problem: the depth it reports is the depth of the point it found, so the silhouette is the
// surface's silhouette and the intersection with the blade is resolved per pixel by the depth test,
// exactly like real geometry. SV_Depth here is load bearing, not an optimisation.
//
// GEOMETRY
// --------
// One box covering the patch, drawn INSIDE OUT (Cull Front). The camera sits 1.15 m up and routinely
// ends up inside the volume, and a back-face box is the only version of this that survives that. The
// fragment's world position is therefore the ray's EXIT from the box, never its entry, so the entry is
// recovered analytically with a slab test instead of being read off the geometry.
//
// THE MARCH DOES NOT HOLE, AND THAT IS A PROOF
// --------------------------------------------
// 1. The volume actually marched is not the proxy box: its top is the measured crest of the whole field
//    plus a margin, fed in from a GPU reduction. From a 1.15 m camera a ray pitched 3 deg below
//    horizontal would need ~20 m of march to descend from a 1.5 m box top to a 30 cm surface, which no
//    sane step budget covers.
// 2. _CoarseMaxTex bounds the surface over a box of half-width _CoarseSafeRadiusM around any sample. If
//    the ray is d above that bound it can advance by d / |rd.y| before it could descend to it, and by
//    _CoarseSafeRadiusM before it could leave the region the bound covers; min() of the two is an advance
//    across which NO crossing can exist.
// 3. The step is never allowed to be so small that the loop runs out before the box exit.
//
// V6'S CASUAL CHANGES TO THE SURFACE, AND WHY THE BOUND SURVIVES THEM
// -------------------------------------------------------------------
// SurfaceY gains three presentation-only terms, all BOUNDED, but the bound is now carried in TWO
// different ways and the difference is the whole reason the lobes are affordable:
//   * SnowCasualRoundH soft-maxes the field against its own dilated coarse max, which fills a convex
//     shoulder with a rounded fillet and leaves flat ground alone. That is the "fat edges" read: a
//     casual wall top is round, a realistic one is crisp. Its lift is hard clamped to _SnowRoundM, and
//     that clamp is reported to the C# side and added to _CoarseMaxBiasM.
//   * SnowCasualLoadLift raises material above the virgin slab depth by a factor that is NEUTRAL AT 1.0,
//     hard clamped to _SnowLoadLiftMaxM, and reported the same way. Off by default.
//   * THE LUMP LATTICE - v4's lobed ScreenSpaceLumps read, a hash-jittered lattice of sphere caps
//     maxed over a 3x3 neighbourhood and converted into a HEIGHT contribution so the surface stays
//     h(x,z) - is now BAKED into a 6.25 cm single-channel texture by the LumpBake kernel and read
//     here with ONE bilinear tap. It is NOT in _CoarseMaxBiasM: the coarse-max build maxes the baked
//     texels over each coarse cell, so the skip's bound is the lift that is actually there rather
//     than a blanket + r over the whole 120 x 110 m field. See CoarseMaxYFrom in SnowMarchCore.hlsl
//     for the upper-bound argument and SnowLumpLattice.hlsl for the formula and the encoding.
// All three are faded out as the snow thins - the first two by the same `thin` term the detail uses,
// the lump term by a depth gate that is now baked into its texture - which is load bearing rather than
// cosmetic: without it bare ground inside the swept lane would be lifted, and the lane would
// visually refill while the simulation still says it is clear.
//
// The shading is v3's, computed in full, and then handed to SnowCasualApply. _SnowCasual = 0 returns the
// v3 image bit for bit, which is what makes this a look A/B rather than a second variant.
//
// Every non-void function here has exactly ONE return: the project convention for this variant.
// It is CONSERVATIVE rather than the actual Metal constraint - see the METAL note in
// SnowCasualStyle.hlsl for the real failure (an early return that leaves an out parameter
// unwritten), the six live counter-examples in v3, and why a clean single-return audit is
// compliance rather than proof.
Shader "PPack/SnowRaymarch"
{
    Properties
    {
        _BaseColor    ("Base Color", Color) = (0.94, 0.96, 1.00, 1)
        _DeepColor    ("Deep Color", Color) = (0.55, 0.63, 0.80, 1)
        _AmbientColor ("Ambient",    Color) = (0.34, 0.40, 0.54, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType"     = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
        }

        HLSLINCLUDE

        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // The surface, the field sampling and MarchSnow itself. Shared verbatim with the steps probe
        // compute kernel, so the instrument cannot measure a different march from the one that draws.
        // SnowCasualStyle.hlsl comes in through it.
        #include "SnowMarchCore.hlsl"

        // ------------------------------------------------------------------ ray setup, RENDER ONLY
        //
        // Everything below needs a rendering context - view and projection matrices, the near plane,
        // the main light - which is exactly why it is here and not in the shared core.

        // Recovers the ray and the segment of it inside the MARCH volume (the proxy box with its top
        // pulled down to _MarchTopY).
        //
        // Handles orthographic projection because the ShadowCaster pass is orthographic: there the camera
        // position carries no direction information, so the ray direction comes from the view matrix and
        // the fragment's own position is the origin, which puts the box entry at a negative t.
        // Orthographic-ness is read off the projection matrix rather than unity_OrthoParams, which is not
        // reliably rebound for the shadow pass.
        bool SetupRay(float3 positionWS, out float3 ro, out float3 rd, out float tStart, out float tEnd)
        {
            // The floor is a hair ABOVE the ground plane, not on it. Every real crossing is at least
            // _MinSnowHeight up, so nothing is lost - but a ray that exits still exits above the ground
            // plane, which stops the lane edge producing hits coplanar with it.
            float3 bmin = float3(_BoxMin.x, _MarchFloorY, _BoxMin.z);
            float3 bmax = float3(_BoxMax.x, min(_MarchTopY, _BoxMax.y), _BoxMax.z);

            float t0, t1;
            bool  ok;

            if (abs(UNITY_MATRIX_P._m33) > 0.5)
            {
                rd = -normalize(float3(UNITY_MATRIX_V._m20, UNITY_MATRIX_V._m21, UNITY_MATRIX_V._m22));
                ro = positionWS;

                ok     = IntersectBox(ro, rd, bmin, bmax, t0, t1);
                tStart = t0;
                tEnd   = min(t1, 0.0);
            }
            else
            {
                ro = _WorldSpaceCameraPos;

                float3 d   = positionWS - ro;
                float  len = length(d);
                rd = d / max(len, 1e-6);

                ok     = IntersectBox(ro, rd, bmin, bmax, t0, t1);

                // Clamped to the NEAR PLANE, not to zero. When the camera is inside the snow - and it can
                // be, the camera sits at 1.15 m and the capped pile reaches 0.65 m plus its fillet - a hit
                // at t = 0 is a hit AT the camera, where clip.w is zero and the depth divide blows up.
                tStart = max(t0, _ProjectionParams.y);
                tEnd   = min(t1, len);
            }

            return ok && tEnd > tStart;
        }

        // ------------------------------------------------------------------ depth

        float DepthFromWorld(float3 p)
        {
            float4 clip = mul(UNITY_MATRIX_VP, float4(p, 1.0));
            return clip.z / clip.w;
        }

        // ------------------------------------------------------------------ shading

        // Central differences plus the Laplacian, from one shared set of taps. The taps are clamped
        // relative to the centre because a tap can fall on bare ground, where SurfaceY returns its "no
        // surface" sentinel a kilometre down; unclamped, one such tap flips the normal and the lane edge
        // gets a black fringe.
        float3 SurfaceNormal(float2 xz, float2 lodFade, out float curvature)
        {
            float e = _NormalEpsM;

            float c  = SurfaceY(xz, lodFade);
            float lo = c - _NormalClampM;
            float hi = c + _NormalClampM;

            float px = clamp(SurfaceY(xz + float2(e, 0.0), lodFade), lo, hi);
            float nx = clamp(SurfaceY(xz - float2(e, 0.0), lodFade), lo, hi);
            float pz = clamp(SurfaceY(xz + float2(0.0, e), lodFade), lo, hi);
            float nz = clamp(SurfaceY(xz - float2(0.0, e), lodFade), lo, hi);

            // curvature stays the curvature of the MARCHED surface, deliberately: it drives the crease AO,
            // and a term whose Laplacian is dominated by a 4 cm noise would saturate that AO everywhere.
            curvature = (px + nx + pz + nz) * 0.25 - c;

            // The sharp normal-only term enters here and only here, as a slope perturbation. v6 defaults
            // its amplitude to 0, so these two differences cost four noise evaluations that multiply by
            // zero - the compiler cannot fold them because the amplitude is a uniform, which is a fair
            // price for keeping v3's crispness one field edit away.
            float ampN = _NormalDetailAmpM * lodFade.x
                       * saturate((SampleFieldH(xz) - _MinSnowHeight) * _DetailThinInv);

            float dnx = NormalDetailFbm(xz + float2(e, 0.0)) - NormalDetailFbm(xz - float2(e, 0.0));
            float dnz = NormalDetailFbm(xz + float2(0.0, e)) - NormalDetailFbm(xz - float2(0.0, e));

            return normalize(float3((nx - px) - dnx * ampN, 2.0 * e, (nz - pz) - dnz * ampN));
        }

        // Coarse curvature of the SMOOTH field only - no noise, four cheap texture taps. This is the term
        // that darkens the trough between two berms and the root of the front pile.
        float FieldCurvature(float2 xz)
        {
            float e = _AoWideEpsM;
            float c = SampleFieldH(xz);
            float s = SampleFieldH(xz + float2(e, 0.0)) + SampleFieldH(xz - float2(e, 0.0))
                    + SampleFieldH(xz + float2(0.0, e)) + SampleFieldH(xz - float2(0.0, e));
            return s * 0.25 - c;
        }

        // Short march toward the sun. Not a shadow map: a clearance-over-distance ratio kept at its
        // tightest pinch, which is a penumbra estimate for free. This is the single biggest thing that
        // makes the front pile read as a mass rather than as a lit blob - and in v6 it is ALSO what the
        // banded ramp quantises, so the bands land on the pile's own form rather than only on its normal.
        float SoftShadow(float3 p, float3 L, float2 lodFade)
        {
            int steps = max(0, (int)_SoftShadowSteps);

            float s = 1.0;
            float t = _SoftShadowStartM;

            [loop]
            for (int i = 0; i < steps; ++i)
            {
                float3 q = p + L * t;
                if (q.y > _MarchTopY) break;      // above everything: nothing left that could occlude

                float d = q.y - SurfaceY(q.xz, lodFade);
                s = min(s, saturate(d * _SoftShadowHardness / t));

                t += _SoftShadowStepM;
            }

            // Written as a lerp from 1 rather than as an early return for steps == 0, so this function has
            // one exit; with steps == 0 the loop never runs and s stays 1, which is the same answer.
            return lerp(1.0, s, _SoftShadowStrength);
        }

        half4 ShadeSnow(float3 p, float2 lodFade)
        {
            float curvature;
            float3 n = SurfaceNormal(p.xz, lodFade, curvature);

            float3 L = _MainLightPosition.xyz;
            L = (dot(L, L) > 1e-4) ? normalize(L) : normalize(float3(0.35, 0.85, 0.40));

            float shadow = SoftShadow(p + n * _ShadowNormalBiasM, L, lodFade);

            float ao = 1.0 - _AoStrength * saturate(
                  saturate(curvature * _AoScaleInv) * 0.55
                + saturate(FieldCurvature(p.xz) * _AoWideScaleInv) * 0.45);

            // ---- v3's realistic shading, computed in full so _SnowCasual = 0 reproduces it exactly -----
            float wrap = saturate((dot(n, L) + _Wrap) / (1.0 + _Wrap));

            float3 V = normalize(_WorldSpaceCameraPos - p);
            float3 H = normalize(L + V);
            float  spec = pow(saturate(dot(n, H)), _SheenPower) * _Sheen;

            float  wall   = saturate((1.0 - n.y) * _WallTintInv);
            float3 albedo = lerp(_BaseColor.rgb, _DeepColor.rgb, wall * _WallTint) * ao;

            float3 amb = _AmbientColor.rgb * (0.55 + 0.45 * saturate(n.y * 0.5 + 0.5));
            float3 realistic = albedo * (_MainLightColor.rgb * (_Fill + (1.0 - _Fill) * wrap * shadow) + amb)
                             + _MainLightColor.rgb * (spec * shadow);

            // ---- and the casual treatment on top, blended by one global --------------------------------
            //
            // The band term gets a MACRO normal: the smooth field only, over a wide finite difference, with
            // neither the procedural detail nor the fillet in it. Quantising the shading normal instead put
            // a band edge around every one of the ~38 cm detail bumps and the snow field came out covered in
            // concentric contours like a topographic map. _SnowBandNormalWideM 0 passes the shading normal
            // and reproduces that terracing exactly, which is the A/B.
            float3 nBand = (_SnowBandNormalWideM > 1e-4)
                ? FieldMacroNormal(p.xz, _SnowBandNormalWideM)
                : n;

            float3 col = SnowCasualApply(realistic, albedo, n, nBand, L, V,
                                         shadow, ao, p, _MainLightColor.rgb, amb);

            return half4(col, 1.0);
        }

        // ------------------------------------------------------------------ shared vertex stage

        struct Attributes
        {
            float3 positionOS : POSITION;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
        };

        Varyings Vert(Attributes IN)
        {
            Varyings OUT;
            OUT.positionWS = TransformObjectToWorld(IN.positionOS);
            OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
            return OUT;
        }

        // One entry point for every pass, so the depth a pass writes can never disagree with the depth
        // another pass writes for the same pixel. That matters more than it looks: if the URP renderer has
        // depth priming enabled it primes with the DepthOnly pass and then draws the forward pass with
        // ZTest Equal, and any disagreement between the two would erase the snow.
        //
        // The march instrument goes through here as well, deliberately: measuring a REPLICA of the march
        // measures the replica. `marched` distinguishes "the ray never entered the volume, so no march
        // happened and this pixel must not be counted" from "the ray marched and used zero steps", which
        // are not the same population and averaging them together would understate the cost.
        bool FindHit(float3 positionWS, out float3 hitWS, out float3 rdOut, out float2 lodFade,
                     out int steps, out float exhausted, out float marched)
        {
            float3 ro, rd;
            float  tStart, tEnd;

            hitWS     = positionWS;
            rdOut     = float3(0.0, -1.0, 0.0);
            lodFade   = float2(1.0, 1.0);
            steps     = 0;
            exhausted = 0.0;
            marched   = 0.0;

            bool found = false;
            if (SetupRay(positionWS, ro, rd, tStart, tEnd))
            {
                rdOut   = rd;
                lodFade = MarchLodFades(max(0.0, tStart));
                marched = 1.0;

                float tHit;
                int   s;
                float ex;
                found = MarchSnow(ro, rd, tStart, tEnd, lodFade, tHit, s, ex);
                hitWS = ro + rd * tHit;
                steps = s;
                exhausted = ex;
            }

            // Single exit. v3 returned early from both failure branches; this is the same logic with the
            // results folded into one flag, which keeps the file's single-return rule intact even though
            // this particular function is one Metal happens to accept either way.
            return found;
        }

        // Convenience wrapper for the passes that do not want the instrument outputs.
        //
        // A DISTINCT NAME rather than an overload. HLSL does permit overloading and DXC resolves this
        // one unambiguously, but this file's whole Metal discipline is "do not rely on the front end
        // agreeing with the back end about something you did not have to ask it".
        bool FindHitPlain(float3 positionWS, out float3 hitWS, out float3 rdOut, out float2 lodFade)
        {
            int   s;
            float ex, mr;
            return FindHit(positionWS, hitWS, rdOut, lodFade, s, ex, mr);
        }

        // Blue -> green -> yellow -> red over 0..1, for the steps heat view. No branches, one exit.
        float3 SnowStepsHeat(float t)
        {
            float x = saturate(t);
            float3 c = float3(saturate(x * 3.0 - 1.0),
                              saturate(1.5 - abs(x * 3.0 - 1.5)),
                              saturate(1.0 - x * 3.0));
            return c + float3(0.05, 0.05, 0.05);
        }

        ENDHLSL

        // -----------------------------------------------------------------------------------------
        // Forward. Shades the hit and writes its true depth, which is what buys the correct silhouette
        // and the correct intersection with the blade.
        // -----------------------------------------------------------------------------------------
        Pass
        {
            Name "SnowRaymarchForward"
            Tags { "LightMode" = "UniversalForward" }

            ZWrite On
            ZTest LEqual
            Cull Front            // inside faces, so the effect survives the camera entering the box

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma target 4.5
            #pragma fragment FragForward

            // The steps heat view. 0 = normal shading, 1 = the march cost as a colour. It is a uniform,
            // so the branch is scalar across the whole draw, and the shading call stays guarded so a
            // heat capture does not also pay for a normal and a self-shadow march on every pixel.
            float _StepsHeatMode;

            half4 FragForward(Varyings IN, out float outDepth : SV_Depth) : SV_Target
            {
                float3 hitWS, rd;
                float2 lodFade;
                int    steps;
                float  exhausted, marched;

                bool hit = FindHit(IN.positionWS, hitWS, rd, lodFade, steps, exhausted, marched);

                outDepth = hit ? DepthFromWorld(hitWS) : 0.0;

                // A miss is discarded, so the proxy box is never drawn and never has to be transparent,
                // and discarding also suppresses the depth write - which is what stops the proxy geometry
                // from occluding anything.
                //
                // clip() rather than `discard` inside an if, so this function has ONE exit. The shading is
                // still guarded by `hit`: at this camera a third to a half of the screen is sky and bare
                // ground, and evaluating ShadeSnow on those pixels would pay for a normal (five surface
                // evaluations) and a self-shadow march per discarded fragment. Folding the two paths into a
                // ternary would do exactly that, because both sides of a ternary may be evaluated.
                clip(hit ? 1.0 : -1.0);

                half4 col = half4(0.0, 0.0, 0.0, 0.0);
                if (hit && _StepsHeatMode < 0.5) col = ShadeSnow(hitWS, lodFade);
                if (hit && _StepsHeatMode >= 0.5)
                {
                    float t = (float)steps / max(1.0, _MaxSteps);
                    col = half4(SnowStepsHeat(t), 1.0);
                }
                return col;
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------------------
        // DepthOnly. Present so the snow exists in _CameraDepthTexture and so depth priming, if the URP
        // asset turns it on, primes with the same values the forward pass will compute. Same march, same
        // depth expression, no shading - and therefore no casual uniform can move it.
        // -----------------------------------------------------------------------------------------
        Pass
        {
            Name "SnowRaymarchDepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ZTest LEqual
            Cull Front
            ColorMask R

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma target 4.5
            #pragma fragment FragDepthOnly

            half4 FragDepthOnly(Varyings IN, out float outDepth : SV_Depth) : SV_Target
            {
                float3 hitWS, rd;
                float2 lodFade;

                bool hit = FindHitPlain(IN.positionWS, hitWS, rd, lodFade);

                // Bit-identical to the forward pass's expression, deliberately: if the URP renderer has
                // depth priming enabled it primes with THIS pass and then draws forward with ZTest Equal,
                // and any disagreement between the two would erase the snow entirely.
                outDepth = hit ? DepthFromWorld(hitWS) : 0.0;

                clip(hit ? 1.0 : -1.0);
                return half4(outDepth, 0.0, 0.0, 0.0);
            }
            ENDHLSL
        }

        // -----------------------------------------------------------------------------------------
        // ShadowCaster. The same march from the light's orthographic point of view, writing only depth.
        // Off by default: the renderer only sets shadowCastingMode when the toggle is on. A wrong shadow
        // is worse than no shadow, and this one costs a second full march of the volume - and the snow
        // does not sample the shadow map itself (it has its own soft self-shadow), so turning it on makes
        // the snow shadow the ground and the blade but not itself.
        //
        // The bias is metric and applied to the hit point along the light ray rather than through URP's
        // ApplyShadowBias, because the depth here comes from the fragment, not from the vertex, so a
        // vertex-stage bias would not reach it.
        // -----------------------------------------------------------------------------------------
        Pass
        {
            Name "SnowRaymarchShadow"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            Cull Front
            ColorMask 0

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma target 4.5
            #pragma fragment FragShadow

            void FragShadow(Varyings IN, out float outDepth : SV_Depth)
            {
                float3 hitWS, rd;
                float2 lodFade;

                if (!FindHitPlain(IN.positionWS, hitWS, rd, lodFade))
                {
                    outDepth = 0.0;
                    discard;
                    return;
                }

                // Pull the caster toward the light so a receiver on that same surface does not shadow
                // itself. rd points away from the light, so subtracting moves toward it.
                outDepth = DepthFromWorld(hitWS - rd * _ShadowDepthBiasM);
            }
            ENDHLSL
        }

    }

    Fallback Off
}

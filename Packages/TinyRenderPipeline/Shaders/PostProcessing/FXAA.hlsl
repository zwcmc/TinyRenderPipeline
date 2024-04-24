#ifndef TINY_RP_FXAA_INCLUDED
#define TINY_RP_FXAA_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

float4 _SourceSize; // (x: screenWidthInPixels, y: screenHeightInPixels, z: 1.0/screenWidthInPixels, w: 1.0/screenHeightInPixels)

/*----------------*/

// Choose the amount of sub-pixel aliasing removal.
// This can effect sharpness.
//   1.00 - upper limit (softer)
//   0.75 - default amount of filtering
//   0.50 - lower limit (sharper, less sub-pixel aliasing removal)
//   0.25 - almost off
//   0.00 - completely off
#define FXAA_QUALITY__SUBPIX 0.75

// The minimum amount of local contrast required to apply algorithm.
//   0.333 - too little (faster)
//   0.250 - low quality
//   0.166 - default
//   0.125 - high quality
//   0.063 - overkill (slower)
// Using same value as URP
#define FXAA_QUALITY__EDGE_THRESHOLD 0.15

// Trims the algorithm from processing darks.
//   0.0833 - upper limit (default, the start of visible unfiltered edges)
//   0.0625 - high quality (faster)
//   0.0312 - visible limit (slower)
// Special notes when using FXAA_GREEN_AS_LUMA,
//   Likely want to set this to zero.
//   As colors that are mostly not-green
//   will appear very dark in the green channel!
//   Tune by looking at mostly non-green content,
//   then start at zero and increase until aliasing is a problem.
// Using same value as URP
#define FXAA_QUALITY__EDGE_THRESHOLD_MIN 0.03

#define FXAA_EXTRA_EDGE_SEARCH_STEPS 5
#define FXAA_EDGE_SEARCH_STEP0 1.0
#define FXAA_EXTRA_EDGE_SEARCH_STEP_SIZES 1.5, 2.0, 2.0, 2.0, 8.0
static const float SEARCH_STEPS[FXAA_EXTRA_EDGE_SEARCH_STEPS] = { FXAA_EXTRA_EDGE_SEARCH_STEP_SIZES };

/*----------------*/

float FXAALuma(float4 rgba)
{
    return dot(rgba.xyz, float3(0.299, 0.587, 0.114));
}

half4 FXAATexOff(float2 uv, int2 offset)
{
    float2 rcpPixels = _SourceSize.zw;
    return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv + offset * rcpPixels, 0.0);
}

half4 FXAATex(float2 uv)
{
    return SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, uv, 0.0);
}

half4 FragFXAA(Varyings input) : SV_Target
{
    // Center pixel
    float2 posM = input.texcoord;

    // Center sample
    half4 rgbyM = SAMPLE_TEXTURE2D_LOD(_BlitTexture, sampler_LinearClamp, posM, 0.0);

    // Luma of pixels around current pixel
    float lumaM = FXAALuma(rgbyM);

    float lumaS = FXAALuma(FXAATexOff(posM, int2(0, -1)));
    float lumaE = FXAALuma(FXAATexOff(posM, int2(1, 0)));
    float lumaN = FXAALuma(FXAATexOff(posM, int2(0, 1)));
    float lumaW = FXAALuma(FXAATexOff(posM, int2(-1, 0)));

    // 1. Find edge
    //         N
    //      W  M  E
    //         S
    /*----------------*/
    float maxSM = max(lumaS, lumaM);
    float minSM = min(lumaS, lumaM);
    float maxESM = max(lumaE, maxSM);
    float minESM = min(lumaE, minSM);
    float maxWN = max(lumaW, lumaN);
    float minWN = min(lumaW, lumaN);
    // Maximum luminance of pixels
    float rangeMax = max(maxWN, maxESM);
    // Minimum luminance of pixels
    float rangeMin = min(minWN, minESM);
    float rangeMaxScaled = rangeMax * FXAA_QUALITY__EDGE_THRESHOLD;
    // Get the edge
    float range = rangeMax - rangeMin;
    // Skip the pixel that its neighborhood does not have a high enough contrast
    float rangeMaxClamped = max(FXAA_QUALITY__EDGE_THRESHOLD_MIN, rangeMaxScaled);
    if (range < rangeMaxClamped)
        return rgbyM;
    /*----------------*/

    // 2. More pixels to indicate the direction of the edge, horizontal edge or vertical edge
    //      NW  N  NE
    //      W   M  E
    //      SW  S  SE
    // horizontal weight = abs((N + S) - 2.0 * M)) * 2.0 + abs((NE + SE) - 2.0 * E)) + abs((NW + SW) - 2.0 * W))
    // vertical weight = abs((W + E) - 2.0 * M)) * 2.0 + abs((NW + NE) - 2.0 * N)) + abs((SW + SE) - 2.0 * S))
    /*----------------*/
    float lumaNW = FXAALuma(FXAATexOff(posM, int2(-1, 1)));
    float lumaSE = FXAALuma(FXAATexOff(posM, int2(1, -1)));
    float lumaNE = FXAALuma(FXAATexOff(posM, int2(1, 1)));
    float lumaSW = FXAALuma(FXAATexOff(posM, int2(-1, -1)));

    float lumaNS = lumaN + lumaS;
    float lumaWE = lumaW + lumaE;
    float edgeHorz1 = (-2.0 * lumaM) + lumaNS;
    float edgeVert1 = (-2.0 * lumaM) + lumaWE;

    float lumaNESE = lumaNE + lumaSE;
    float lumaNWNE = lumaNW + lumaNE;
    float edgeHorz2 = (-2.0 * lumaE) + lumaNESE;
    float edgeVert2 = (-2.0 * lumaN) + lumaNWNE;

    float lumaNWSW = lumaNW + lumaSW;
    float lumaSWSE = lumaSW + lumaSE;
    float edgeHorz4 = abs(edgeHorz1) * 2.0 + abs(edgeHorz2);
    float edgeVert4 = abs(edgeVert1) * 2.0 + abs(edgeVert2);

    float edgeHorz3 = (-2.0 * lumaW) + lumaNWSW;
    float edgeVert3 = (-2.0 * lumaS) + lumaSWSE;

    float edgeHorz = abs(edgeHorz3) + edgeHorz4;
    float edgeVert = abs(edgeVert3) + edgeVert4;

    bool horzSpan = edgeHorz >= edgeVert;
    /*----------------*/

    // 3. Calculate subpixel blend factor
    // Neighbors weights
    //      1  2  1
    //      2  M  2
    //      1  2  1
    /*----------------*/
    // Total luminance of the neighborhood according to neighbor weights
    float subpixNSWE = lumaNS + lumaWE;
    float subpixNWSWNESE = lumaNWSW + lumaNESE;
    float subpixA = subpixNSWE * 2.0 + subpixNWSWNESE;

    // Calculate average of all adjacent neighbors and get the contrast between the middle and this average
    float subpixB = (subpixA * (1.0 / 12.0)) - lumaM;

    // Normalized the contrast, clamp the result to a maximum of 1
    float subpixRcpRange = 1.0 / range;
    float subpixC = saturate(abs(subpixB) * subpixRcpRange);

    // Make factor more smoother

    // two different ways below:
    // [1] used in URP FXAA3_11.hlsl (FXAA_PC == 1)
    // float subpixD = ((-2.0) * subpixC) + 3.0;
    // float subpixE = subpixC * subpixC;
    // float subpixF = subpixD * subpixE;
    // float subpixG = subpixF * subpixF;
    // float subpixH = subpixG * FXAA_QUALITY__SUBPIX;

    // [2] used in FXAA tutorial from Catlike Coding's Custom SRP
    float subpixD = smoothstep(0, 1, subpixC);
    float subpixH = subpixD * subpixD * FXAA_QUALITY__SUBPIX;
    /*----------------*/

    // Step length: horizontal edge, 1.0 / screenHeightInPixels, vertical edge, 1.0 / screenWidthInPixels
    float lengthSign = _SourceSize.z;
    if (horzSpan)
        lengthSign = _SourceSize.w;

    // Calculate the gradient of positive direction and negative direction
    // N means positive direction and S means negative direction
    // horizontal edge: positive = north, negative = south, vertical edge: positive = east, negative = west
    if (!horzSpan)
    {
        lumaN = lumaE;
        lumaS = lumaW;
    }
    float gradientN = lumaN - lumaM;
    float gradientS = lumaS - lumaM;

    // Two direction gradients indicate step length direction
    bool pairN = abs(gradientN) >= abs(gradientS);
    if (!pairN) lengthSign = -lengthSign;

    // Determine the start UV coordinates on the edge between pixels, which is half step length away from the original UV coordinates
    float2 posB;
    posB.x = posM.x;
    posB.y = posM.y;
    if (!horzSpan) posB.x += lengthSign * 0.5;
    if (horzSpan) posB.y += lengthSign * 0.5;

    // Search step offset
    float2 offNP;
    offNP.x = (!horzSpan) ? 0.0 : _SourceSize.z;
    offNP.y = (horzSpan) ? 0.0 : _SourceSize.w;

    // Gradient threshold for determining that searching has gone off the edge
    float gradient = max(abs(gradientN), abs(gradientS));
    float gradientScaled = gradient * 1.0 / 4.0;

    // 4. First step search in the negative direction and the positive direction
    /*----------------*/
    // Move UV coordinates
    float2 posN;
    posN.x = posB.x - offNP.x * FXAA_EDGE_SEARCH_STEP0;
    posN.y = posB.y - offNP.y * FXAA_EDGE_SEARCH_STEP0;
    float2 posP;
    posP.x = posB.x + offNP.x * FXAA_EDGE_SEARCH_STEP0;
    posP.y = posB.y + offNP.y * FXAA_EDGE_SEARCH_STEP0;

    // Get luminance
    float lumaEndN = FXAALuma(FXAATex(posN));
    float lumaEndP = FXAALuma(FXAATex(posP));

    float lumaNN = lumaN + lumaM;
    float lumaSS = lumaS + lumaM;
    if (!pairN)
        lumaNN = lumaSS;

    // Calculate the luminance gradient between that offset and the original edge
    lumaEndN -= lumaNN * 0.5;
    lumaEndP -= lumaNN * 0.5;

    // Whether is at the end of the edge
    bool doneN = abs(lumaEndN) >= gradientScaled;
    bool doneP = abs(lumaEndP) >= gradientScaled;

    bool doneNP = (!doneN) || (!doneP);
    /*----------------*/

    // 5. Loop step searching...
    /*----------------*/
    UNITY_UNROLL
    for (int i = 0; i < FXAA_EXTRA_EDGE_SEARCH_STEPS && doneNP; ++i)
    {
        if (!doneN) posN.x -= offNP.x * SEARCH_STEPS[i];
        if (!doneN) posN.y -= offNP.y * SEARCH_STEPS[i];
        if (!doneP) posP.x += offNP.x * SEARCH_STEPS[i];
        if (!doneP) posP.y += offNP.y * SEARCH_STEPS[i];

        if (!doneN) lumaEndN = FXAALuma(FXAATex(posN));
        if (!doneP) lumaEndP = FXAALuma(FXAATex(posP));
        if (!doneN) lumaEndN -= lumaNN * 0.5;
        if (!doneP) lumaEndP -= lumaNN * 0.5;

        doneN = abs(lumaEndN) >= gradientScaled;
        doneP = abs(lumaEndP) >= gradientScaled;

        doneNP = (!doneN) || (!doneP);
    }
    /*----------------*/

    // Distances to each end point of the edge
    float dstN = posM.x - posN.x;
    float dstP = posP.x - posM.x;
    if (!horzSpan) dstN = posM.y - posN.y;
    if (!horzSpan) dstP = posP.y - posM.y;

    // Luminance gradient in original UV coordinates
    float lumaMM = lumaM - lumaNN * 0.5;
    // Need blend only if luminance gradient is different between the original UV coordinates and the end UV coordinates
    // do not understand now ?

    // Determine whether need to do edge blend of two directions
    bool lumaMLTZero = lumaMM < 0.0;
    bool goodSpanN = (lumaEndN < 0.0) != lumaMLTZero;
    bool goodSpanP = (lumaEndP < 0.0) != lumaMLTZero;

    // Choose the closer direction of the end edge
    bool directionN = dstN < dstP;
    float dst = min(dstN, dstP);
    bool goodSpan = directionN ? goodSpanN : goodSpanP;

    // Edge length
    float spanLength = (dstP + dstN);
    float spanLengthRcp = 1.0 / spanLength;

    // Calculate edge blend factor
    float pixelOffset = (dst * (-spanLengthRcp)) + 0.5;
    float pixelOffsetGood = goodSpan ? pixelOffset : 0.0;

    // Apply both edge and subpixel blending, use the largest blend factor of both
    float pixelOffsetSubpix = max(pixelOffsetGood, subpixH);

    if (!horzSpan) posM.x += pixelOffsetSubpix * lengthSign;
    if (horzSpan) posM.y += pixelOffsetSubpix * lengthSign;

    return half4(FXAATex(posM).rgb, 1.0);
}

#endif

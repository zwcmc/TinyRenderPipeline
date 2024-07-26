using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile(FloatMode = FloatMode.Default, DisableSafetyChecks = true, OptimizeFor = OptimizeFor.Performance)]
struct TilingJob : IJobFor
{
    // 所有额外光源
    [ReadOnly]
    public NativeArray<VisibleLight> lights;

    // 所有额外光源的范围
    [NativeDisableParallelForRestriction]
    public NativeArray<InclusiveRange> tileRanges;

    // 额外光源数量
    public int itemsPerTile;
    // 每个光源拥有的范围数量
    public int rangesPerLight;

    // 世界空间到观察空间的转换矩阵
    public float4x4 worldToView;

    public float2 tileScale;
    public float2 tileScaleInv;
    public float viewPlaneBottom;
    public float viewPlaneTop;
    public float4 viewToViewportScaleBias;

    // x，y分别存储的是屏幕
    public int2 tileCount;
    // 相机的近平面
    public float near;
    // 相机是正交相机还是透视相机
    public bool isOrthographic;

    // 当前光源在屏幕 Y 轴方向覆盖的 Tile 范围
    private InclusiveRange m_TileYRange;
    // 当前光源在 tileRanges 中的起始索引
    private int m_Offset;

    public void Execute(int jobIndex)
    {
        // 额外光源的索引
        var index = jobIndex % itemsPerTile;

        // 根据额外光源的索引计算出此光源在 tileRanges 中的起始索引
        m_Offset = jobIndex * rangesPerLight;

        // 初始化所有范围数据
        m_TileYRange = new InclusiveRange(short.MaxValue, short.MinValue);
        for (int i = 0; i < rangesPerLight; i++)
        {
            tileRanges[m_Offset + i] = new InclusiveRange(short.MaxValue, short.MinValue);
        }

        // 根据相机是正交相机还是透视相机分开处理每个光源
        if (isOrthographic)
            TileLightOrthographic(index);
        else
            TileLight(index);
    }

    private void TileLightOrthographic(int lightIndex)
    {
        // 当前光源
        var light = lights[lightIndex];
        // 将当前光源原点坐标转换到观察空间
        var lightToWorld = (float4x4)light.localToWorldMatrix;
        var lightPosVS = math.mul(worldToView, math.float4(lightToWorld.c3.xyz, 1)).xyz;
        lightPosVS.z *= -1;

        // 根据观察空间坐标计算出此光源原点是屏幕空间属于哪一个 Tile，具体代码分析在 ExpandOrthographic 函数内
        ExpandOrthographic(lightPosVS);

        // 计算聚光灯光源的方向，将灯光方向转换到观察空间，并归一化此方向
        var lightDirVS = math.mul(worldToView, math.float4(lightToWorld.c2.xyz, 0)).xyz;
        lightDirVS.z *= -1;
        lightDirVS = math.normalize(lightDirVS);

        // 光源属性的一些计算，主要是和聚光灯相关
        var halfAngle = math.radians(light.spotAngle * 0.5f);
        var range = light.range;
        var rangeSq = square(range);
        var cosHalfAngle = math.cos(halfAngle);
        var coneHeight = cosHalfAngle * range;
        var coneHeightSq = square(coneHeight);
        var coneHeightInv = 1f / coneHeight;
        var coneHeightInvSq = square(coneHeightInv);

        // 对观察空间下的光源原点分别向 -Y 轴方向、+Y 轴方向、-X 轴方向和 +Y 轴方向偏移 range 范围的 4 个点
        var sphereBoundY0 = lightPosVS - math.float3(0, range, 0);
        var sphereBoundY1 = lightPosVS + math.float3(0, range, 0);
        var sphereBoundX0 = lightPosVS - math.float3(range, 0, 0);
        var sphereBoundX1 = lightPosVS + math.float3(range, 0, 0);

        bool SpherePointIsValid(float3 p) => light.lightType == LightType.Point || math.dot(math.normalize(p - lightPosVS), lightDirVS) >= cosHalfAngle;

        // 分别计算这 4 个点属于哪一个 Tile
        // 需要注意的是，对于点光源，它光的方向是一个球体，映射到屏幕就是一个圆形，所以通过这 4 个点计算就可以得到此点光源 Y 轴覆盖了哪些 Tile，以及 Y 轴覆盖的每一层 Tile 的 X 轴的范围；
        // 而对于聚光灯光源，只有 math.dot(math.normalize(p - lightPosVS), lightDirVS) >= cosHalfAngle 时，才通过这 4 个点计算此光源覆盖了哪些 Tile，这是为什么：
        //   1. 首先，2 个单位向量点乘，返回的就是这 2 个单位向量夹脚的余弦，而余弦函数夹角为 0 度是结果为 1，随着夹角变大，余弦值变小，到 90 度时，结果为 0；
        //   2. 所以 math.dot(math.normalize(p - lightPosVS), lightDirVS) >= cosHalfAngle 就表示：这个点 p 是在聚光灯的范围内，只有在聚光灯范围内， (p - lightPosVS) 这个向量与聚光灯方向的夹角才会小于一半的聚光灯角度，夹角更小，余弦值才会更大
        //   3. 点 p 在聚光灯范围内时，此时才可以计算此点映射到屏幕后覆盖的 Tile 范围，如果在聚光灯范围外就没有计算的必要了
        if (SpherePointIsValid(sphereBoundY0)) ExpandOrthographic(sphereBoundY0);
        if (SpherePointIsValid(sphereBoundY1)) ExpandOrthographic(sphereBoundY1);
        if (SpherePointIsValid(sphereBoundX0)) ExpandOrthographic(sphereBoundX0);
        if (SpherePointIsValid(sphereBoundX1)) ExpandOrthographic(sphereBoundX1);

        // 聚光灯锥体底面圆的圆心在观察空间坐标
        var circleCenter = lightPosVS + lightDirVS * coneHeight;
        // 底面圆的半径
        var circleRadius = math.sqrt(rangeSq - coneHeightSq);
        var circleRadiusSq = square(circleRadius);
        var circleUp = math.normalize(math.float3(0, 1, 0) - lightDirVS * lightDirVS.y);
        var circleRight = math.normalize(math.float3(1, 0, 0) - lightDirVS * lightDirVS.x);
        var circleBoundY0 = circleCenter - circleUp * circleRadius;
        var circleBoundY1 = circleCenter + circleUp * circleRadius;

        if (light.lightType == LightType.Spot)
        {
            var circleBoundX0 = circleCenter - circleRight * circleRadius;
            var circleBoundX1 = circleCenter + circleRight * circleRadius;
            ExpandOrthographic(circleBoundY0);
            ExpandOrthographic(circleBoundY1);
            ExpandOrthographic(circleBoundX0);
            ExpandOrthographic(circleBoundX1);
        }

        m_TileYRange.Clamp(0, (short)(tileCount.y - 1));

        // Find two lines in screen-space for the cone if the light is a spot.
        float coneDir0X = 0, coneDir0YInv = 0, coneDir1X = 0, coneDir1YInv = 0;
        if (light.lightType == LightType.Spot)
        {
            // Distance from light position to and radius of sphere fitted to the end of the cone.
            var sphereDistance = coneHeight + circleRadiusSq * coneHeightInv;
            var sphereRadius = math.sqrt(square(circleRadiusSq) * coneHeightInvSq + circleRadiusSq);
            var directionXYSqInv = math.rcp(math.lengthsq(lightDirVS.xy));
            var polarIntersection = -circleRadiusSq * coneHeightInv * directionXYSqInv * lightDirVS.xy;
            var polarDir = math.sqrt((square(sphereRadius) - math.lengthsq(polarIntersection)) * directionXYSqInv) * math.float2(lightDirVS.y, -lightDirVS.x);
            var conePBase = lightPosVS.xy + sphereDistance * lightDirVS.xy + polarIntersection;
            var coneP0 = conePBase - polarDir;
            var coneP1 = conePBase + polarDir;

            coneDir0X = coneP0.x - lightPosVS.x;
            coneDir0YInv = math.rcp(coneP0.y - lightPosVS.y);
            coneDir1X = coneP1.x - lightPosVS.x;
            coneDir1YInv = math.rcp(coneP1.y - lightPosVS.y);
        }

        // Tile plane ranges
        for (var planeIndex = m_TileYRange.start + 1; planeIndex <= m_TileYRange.end; planeIndex++)
        {
            var planeRange = InclusiveRange.empty;

            // Sphere
            var planeY = math.lerp(viewPlaneBottom, viewPlaneTop, planeIndex * tileScaleInv.y);
            var sphereX = math.sqrt(rangeSq - square(planeY - lightPosVS.y));
            var sphereX0 = math.float3(lightPosVS.x - sphereX, planeY, lightPosVS.z);
            var sphereX1 = math.float3(lightPosVS.x + sphereX, planeY, lightPosVS.z);
            if (SpherePointIsValid(sphereX0)) { ExpandRangeOrthographic(ref planeRange, sphereX0.x); }
            if (SpherePointIsValid(sphereX1)) { ExpandRangeOrthographic(ref planeRange, sphereX1.x); }

            if (light.lightType == LightType.Spot)
            {
                // Circle
                if (planeY >= circleBoundY0.y && planeY <= circleBoundY1.y)
                {
                    var intersectionDistance = (planeY - circleCenter.y) / circleUp.y;
                    var closestPointX = circleCenter.x + intersectionDistance * circleUp.x;
                    var intersectionDirX = -lightDirVS.z / math.length(math.float3(-lightDirVS.z, 0, lightDirVS.x));
                    var sideDistance = math.sqrt(square(circleRadius) - square(intersectionDistance));
                    var circleX0 = closestPointX - sideDistance * intersectionDirX;
                    var circleX1 = closestPointX + sideDistance * intersectionDirX;
                    ExpandRangeOrthographic(ref planeRange, circleX0);
                    ExpandRangeOrthographic(ref planeRange, circleX1);
                }

                // Cone
                var deltaY = planeY - lightPosVS.y;
                var coneT0 = deltaY * coneDir0YInv;
                var coneT1 = deltaY * coneDir1YInv;
                if (coneT0 >= 0 && coneT0 <= 1) { ExpandRangeOrthographic(ref planeRange, lightPosVS.x + coneT0 * coneDir0X); }
                if (coneT1 >= 0 && coneT1 <= 1) { ExpandRangeOrthographic(ref planeRange, lightPosVS.x + coneT1 * coneDir1X); }
            }

            var tileIndex = m_Offset + 1 + planeIndex;
            tileRanges[tileIndex] = InclusiveRange.Merge(tileRanges[tileIndex], planeRange);
            tileRanges[tileIndex - 1] = InclusiveRange.Merge(tileRanges[tileIndex - 1], planeRange);
        }

        tileRanges[m_Offset] = m_TileYRange;
    }

    private void TileLight(int lightIndex)
    {
        var light = lights[lightIndex];
        if (light.lightType != LightType.Point && light.lightType != LightType.Spot)
        {
            return;
        }

        var lightToWorld = (float4x4)light.localToWorldMatrix;
        var lightPositionVS = math.mul(worldToView, math.float4(lightToWorld.c3.xyz, 1)).xyz;
        lightPositionVS.z *= -1;
        if (lightPositionVS.z >= near) ExpandY(lightPositionVS);
        var lightDirectionVS = math.normalize(math.mul(worldToView, math.float4(lightToWorld.c2.xyz, 0)).xyz);
        lightDirectionVS.z *= -1;

        var halfAngle = math.radians(light.spotAngle * 0.5f);
        var range = light.range;
        var rangesq = square(range);
        var cosHalfAngle = math.cos(halfAngle);
        var coneHeight = cosHalfAngle * range;

        // Radius of circle formed by intersection of sphere and near plane.
        // Found using Pythagoras with a right triangle formed by three points:
        // (a) light position
        // (b) light position projected to near plane
        // (c) a point on the near plane at a distance `range` from the light position
        //     (i.e. lies both on the sphere and the near plane)
        // Thus the hypotenuse is formed by (a) and (c) with length `range`, and the known side is formed
        // by (a) and (b) with length equal to the distance between the near plane and the light position.
        // The remaining unknown side is formed by (b) and (c) with length equal to the radius of the circle.
        // m_ClipCircleRadius = sqrt(sq(light.range) - sq(m_Near - m_LightPosition.z));
        var sphereClipRadius = math.sqrt(rangesq - square(near - lightPositionVS.z));

        // Assumes a point on the sphere, i.e. at distance `range` from the light position.
        // If spot light, we check the angle between the direction vector from the light position and the light direction vector.
        // Note that division by range is to normalize the vector, as we know that the resulting vector will have length `range`.
        bool SpherePointIsValid(float3 p) => light.lightType == LightType.Point ||
            math.dot(math.normalize(p - lightPositionVS), lightDirectionVS) >= cosHalfAngle;

        // Project light sphere onto YZ plane, find the horizon points, and re-construct view space position of found points.
        // CalculateSphereYBounds(lightPositionVS, range, near, sphereClipRadius, out var sphereBoundY0, out var sphereBoundY1);
        GetSphereHorizon(lightPositionVS.yz, range, near, sphereClipRadius, out var sphereBoundYZ0, out var sphereBoundYZ1);
        var sphereBoundY0 = math.float3(lightPositionVS.x, sphereBoundYZ0);
        var sphereBoundY1 = math.float3(lightPositionVS.x, sphereBoundYZ1);
        if (SpherePointIsValid(sphereBoundY0)) ExpandY(sphereBoundY0);
        if (SpherePointIsValid(sphereBoundY1)) ExpandY(sphereBoundY1);

        // Project light sphere onto XZ plane, find the horizon points, and re-construct view space position of found points.
        GetSphereHorizon(lightPositionVS.xz, range, near, sphereClipRadius, out var sphereBoundXZ0, out var sphereBoundXZ1);
        var sphereBoundX0 = math.float3(sphereBoundXZ0.x, lightPositionVS.y, sphereBoundXZ0.y);
        var sphereBoundX1 = math.float3(sphereBoundXZ1.x, lightPositionVS.y, sphereBoundXZ1.y);
        if (SpherePointIsValid(sphereBoundX0)) ExpandY(sphereBoundX0);
        if (SpherePointIsValid(sphereBoundX1)) ExpandY(sphereBoundX1);

        if (light.lightType == LightType.Spot)
        {
            // Cone base
            var baseRadius = math.sqrt(range * range - coneHeight * coneHeight);
            var baseCenter = lightPositionVS + lightDirectionVS * coneHeight;

            // Project cone base (a circle) into the YZ plane, find the horizon points, and re-construct view space position of found points.
            // When projecting a circle to a plane, it becomes an ellipse where the major axis is parallel to the line
            // of intersection of the projection plane and the circle plane. We can get this by taking the cross product
            // of the two plane normals, as the line of intersection will have to be a vector in both planes, and thus
            // orthogonal to both normals.
            // If the two plane normals are parallel, the cross product would return 0. In that case, the circle will
            // project to a line segment, so we pick a vector in the plane pointing in the direction we're interested
            // in finding horizon points in.
            var baseUY = math.abs(math.abs(lightDirectionVS.x) - 1) < 1e-6f ? math.float3(0, 1, 0) : math.normalize(math.cross(lightDirectionVS, math.float3(1, 0, 0)));
            var baseVY = math.cross(lightDirectionVS, baseUY);
            GetProjectedCircleHorizon(baseCenter.yz, baseRadius, baseUY.yz, baseVY.yz, out var baseY1UV, out var baseY2UV);
            var baseY1 = baseCenter + baseY1UV.x * baseUY + baseY1UV.y * baseVY;
            var baseY2 = baseCenter + baseY2UV.x * baseUY + baseY2UV.y * baseVY;
            if (baseY1.z >= near) ExpandY(baseY1);
            if (baseY2.z >= near) ExpandY(baseY2);

            // Project cone base into the XZ plane, find the horizon points, and re-construct view space position of found points.
            // See comment for YZ plane for details.
            var baseUX = math.abs(math.abs(lightDirectionVS.y) - 1) < 1e-6f ? math.float3(1, 0, 0) : math.normalize(math.cross(lightDirectionVS, math.float3(0, 1, 0)));
            var baseVX = math.cross(lightDirectionVS, baseUX);
            GetProjectedCircleHorizon(baseCenter.xz, baseRadius, baseUX.xz, baseVX.xz, out var baseX1UV, out var baseX2UV);
            var baseX1 = baseCenter + baseX1UV.x * baseUX + baseX1UV.y * baseVX;
            var baseX2 = baseCenter + baseX2UV.x * baseUX + baseX2UV.y * baseVX;
            if (baseX1.z >= near) ExpandY(baseX1);
            if (baseX2.z >= near) ExpandY(baseX2);

            // Handle base circle clipping by intersecting it with the near-plane if needed.
            if (GetCircleClipPoints(baseCenter, lightDirectionVS, baseRadius, near, out var baseClip0, out var baseClip1))
            {
                ExpandY(baseClip0);
                ExpandY(baseClip1);
            }

            bool ConicPointIsValid(float3 p) =>
                math.dot(math.normalize(p - lightPositionVS), lightDirectionVS) >= 0 &&
                math.dot(p - lightPositionVS, lightDirectionVS) <= coneHeight;

            // Calculate Z bounds of cone and check if it's overlapping with the near plane.
            // From https://www.iquilezles.org/www/articles/diskbbox/diskbbox.htm
            var baseExtentZ = baseRadius * math.sqrt(1.0f - square(lightDirectionVS.z));
            var coneIsClipping = near >= math.min(baseCenter.z - baseExtentZ, lightPositionVS.z) && near <= math.max(baseCenter.z + baseExtentZ, lightPositionVS.z);

            var coneU = math.cross(lightDirectionVS, lightPositionVS);
            // The cross product will be the 0-vector if the light-direction and camera-to-light-position vectors are parallel.
            // In that case, {1, 0, 0} is orthogonal to the light direction and we use that instead.
            coneU = math.csum(coneU) != 0f ? math.normalize(coneU) : math.float3(1, 0, 0);
            var coneV = math.cross(lightDirectionVS, coneU);

            if (coneIsClipping)
            {
                var r = baseRadius / coneHeight;

                // Find the Y bounds of the near-plane cone intersection, i.e. where y' = 0
                var thetaY = FindNearConicTangentTheta(lightPositionVS.yz, lightDirectionVS.yz, r, coneU.yz, coneV.yz);
                var p0Y = EvaluateNearConic(near, lightPositionVS, lightDirectionVS, r, coneU, coneV, thetaY.x);
                var p1Y = EvaluateNearConic(near, lightPositionVS, lightDirectionVS, r, coneU, coneV, thetaY.y);
                if (ConicPointIsValid(p0Y)) ExpandY(p0Y);
                if (ConicPointIsValid(p1Y)) ExpandY(p1Y);

                // Find the X bounds of the near-plane cone intersection, i.e. where x' = 0
                var thetaX = FindNearConicTangentTheta(lightPositionVS.xz, lightDirectionVS.xz, r, coneU.xz, coneV.xz);
                var p0X = EvaluateNearConic(near, lightPositionVS, lightDirectionVS, r, coneU, coneV, thetaX.x);
                var p1X = EvaluateNearConic(near, lightPositionVS, lightDirectionVS, r, coneU, coneV, thetaX.y);
                if (ConicPointIsValid(p0X)) ExpandY(p0X);
                if (ConicPointIsValid(p1X)) ExpandY(p1X);
            }

            // Calculate the lines making up the sides of the cone as seen from the camera. `l1` and `l2` form lines
            // from the light position.
            GetConeSideTangentPoints(lightPositionVS, lightDirectionVS, cosHalfAngle, baseRadius, coneHeight, range, coneU, coneV, out var l1, out var l2);

            {
                var planeNormal = math.float3(0, 1, viewPlaneBottom);
                var l1t = math.dot(-lightPositionVS, planeNormal) / math.dot(l1, planeNormal);
                var l1x = lightPositionVS + l1 * l1t;
                if (l1t >= 0 && l1t <= 1 && l1x.z >= near) ExpandY(l1x);
            }
            {
                var planeNormal = math.float3(0, 1, viewPlaneTop);
                var l1t = math.dot(-lightPositionVS, planeNormal) / math.dot(l1, planeNormal);
                var l1x = lightPositionVS + l1 * l1t;
                if (l1t >= 0 && l1t <= 1 && l1x.z >= near) ExpandY(l1x);
            }

            m_TileYRange.Clamp(0, (short)(tileCount.y - 1));

            // Calculate tile plane ranges for cone.
            for (var planeIndex = m_TileYRange.start + 1; planeIndex <= m_TileYRange.end; planeIndex++)
            {
                var planeRange = InclusiveRange.empty;

                // Y-position on the view plane (Z=1)
                var planeY = math.lerp(viewPlaneBottom, viewPlaneTop, planeIndex * tileScaleInv.y);

                var planeNormal = math.float3(0, 1, -planeY);

                // Intersect lines with y-plane and clip if needed.
                var l1t = math.dot(-lightPositionVS, planeNormal) / math.dot(l1, planeNormal);
                var l1x = lightPositionVS + l1 * l1t;
                if (l1t >= 0 && l1t <= 1 && l1x.z >= near) planeRange.Expand((short)math.clamp(ViewToTileSpace(l1x).x, 0, tileCount.x - 1));

                var l2t = math.dot(-lightPositionVS, planeNormal) / math.dot(l2, planeNormal);
                var l2x = lightPositionVS + l2 * l2t;
                if (l2t >= 0 && l2t <= 1 && l2x.z >= near) planeRange.Expand((short)math.clamp(ViewToTileSpace(l2x).x, 0, tileCount.x - 1));

                if (IntersectCircleYPlane(planeY, baseCenter, lightDirectionVS, baseUY, baseVY, baseRadius, out var circleTile0, out var circleTile1))
                {
                    if (circleTile0.z >= near) planeRange.Expand((short)math.clamp(ViewToTileSpace(circleTile0).x, 0, tileCount.x - 1));
                    if (circleTile1.z >= near) planeRange.Expand((short)math.clamp(ViewToTileSpace(circleTile1).x, 0, tileCount.x - 1));
                }

                if (coneIsClipping)
                {
                    var y = planeY * near;
                    var r = baseRadius / coneHeight;
                    var theta = FindNearConicYTheta(near, lightPositionVS, lightDirectionVS, r, coneU, coneV, y);
                    var p0 = math.float3(EvaluateNearConic(near, lightPositionVS, lightDirectionVS, r, coneU, coneV, theta.x).x, y, near);
                    var p1 = math.float3(EvaluateNearConic(near, lightPositionVS, lightDirectionVS, r, coneU, coneV, theta.y).x, y, near);
                    if (ConicPointIsValid(p0)) planeRange.Expand((short)math.clamp(ViewToTileSpace(p0).x, 0, tileCount.x - 1));
                    if (ConicPointIsValid(p1)) planeRange.Expand((short)math.clamp(ViewToTileSpace(p1).x, 0, tileCount.x - 1));
                }

                // Write to tile ranges above and below the plane. Note that at `m_Offset` we store Y-range.
                var tileIndex = m_Offset + 1 + planeIndex;
                tileRanges[tileIndex] = InclusiveRange.Merge(tileRanges[tileIndex], planeRange);
                tileRanges[tileIndex - 1] = InclusiveRange.Merge(tileRanges[tileIndex - 1], planeRange);
            }
        }

        m_TileYRange.Clamp(0, (short)(tileCount.y - 1));

        // Calculate tile plane ranges for sphere.
        for (var planeIndex = m_TileYRange.start + 1; planeIndex <= m_TileYRange.end; planeIndex++)
        {
            var planeRange = InclusiveRange.empty;

            var planeY = math.lerp(viewPlaneBottom, viewPlaneTop, planeIndex * tileScaleInv.y);
            GetSphereYPlaneHorizon(lightPositionVS, range, near, sphereClipRadius, planeY, out var sphereTile0, out var sphereTile1);
            if (SpherePointIsValid(sphereTile0)) planeRange.Expand((short)math.clamp(ViewToTileSpace(sphereTile0).x, 0, tileCount.x - 1));
            if (SpherePointIsValid(sphereTile1)) planeRange.Expand((short)math.clamp(ViewToTileSpace(sphereTile1).x, 0, tileCount.x - 1));

            var tileIndex = m_Offset + 1 + planeIndex;
            tileRanges[tileIndex] = InclusiveRange.Merge(tileRanges[tileIndex], planeRange);
            tileRanges[tileIndex - 1] = InclusiveRange.Merge(tileRanges[tileIndex - 1], planeRange);
        }

        tileRanges[m_Offset] = m_TileYRange;
    }

    /// <summary>
    /// Project onto Z=1, scale and offset into [0, tileCount]
    /// </summary>
    float2 ViewToTileSpaceOrthographic(float3 positionVS)
    {
        return (positionVS.xy * viewToViewportScaleBias.xy + viewToViewportScaleBias.zw) * tileScale;
    }

    /// <summary>
    /// Expands the tile Y range and the X range in the row containing the position.
    /// </summary>
    void ExpandOrthographic(float3 positionVS)
    {
        // 通过 viewToViewportScaleBias 讲观察空间的点映射到屏幕空间的 Tile，positionTS.xy 分别是 Tile 的 X 轴索引和 Y 轴索引
        var positionTS = ViewToTileSpaceOrthographic(positionVS);
        var tileY = (int)positionTS.y;
        var tileX = (int)positionTS.x;

        // 将 Y 轴索引限制在 [0, tileCount.y - 1] 并存入 m_TileYRange
        m_TileYRange.Expand((short)math.clamp(tileY, 0, tileCount.y - 1));

        // 如果 X、Y 轴索引都在屏幕中的某个 Tile 中，将此 X 轴索引存入对应的 Y 轴索引这一行中
        if (tileY >= 0 && tileY < tileCount.y && tileX >= 0 && tileX < tileCount.x)
        {
            // m_Offset 是此光源在 tileRanges 中的起始索引，+ 1 是因为第一个索引存储的是此光源在 Y 轴的覆盖 Tiles 的范围，+ tileY 就是表示此光源在 Y 轴的覆盖 Tiles 的范围的 tileY 这一行的范围
            var rowXRange = tileRanges[m_Offset + 1 + tileY];
            // 向这一行范围内存入 X 轴索引 tileX
            rowXRange.Expand((short)tileX);
            // 保存到 tileRanges
            tileRanges[m_Offset + 1 + tileY] = rowXRange;
        }
    }

    static float square(float x) => x * x;

    void ExpandRangeOrthographic(ref InclusiveRange range, float xVS)
    {
        range.Expand((short)math.clamp(ViewToTileSpaceOrthographic(xVS).x, 0, tileCount.x - 1));
    }

    /// <summary>
    /// Project onto Z=1, scale and offset into [0, tileCount]
    /// </summary>
    float2 ViewToTileSpace(float3 positionVS)
    {
        return (positionVS.xy / positionVS.z * viewToViewportScaleBias.xy + viewToViewportScaleBias.zw) * tileScale;
    }

    /// <summary>
    /// Expands the tile Y range and the X range in the row containing the position.
    /// </summary>
    void ExpandY(float3 positionVS)
    {
        // var positionTS = math.clamp(ViewToTileSpace(positionVS), 0, tileCount - 1);
        var positionTS = ViewToTileSpace(positionVS);
        var tileY = (int)positionTS.y;
        var tileX = (int)positionTS.x;
        m_TileYRange.Expand((short)math.clamp(tileY, 0, tileCount.y - 1));
        if (tileY >= 0 && tileY < tileCount.y && tileX >= 0 && tileX < tileCount.x)
        {
            var rowXRange = tileRanges[m_Offset + 1 + tileY];
            rowXRange.Expand((short)tileX);
            tileRanges[m_Offset + 1 + tileY] = rowXRange;
        }
    }

    /// <summary>
    /// Finds the two horizon points seen from (0, 0) of a sphere projected onto either XZ or YZ. Takes clipping into account.
    /// </summary>
    static void GetSphereHorizon(float2 center, float radius, float near, float clipRadius, out float2 p0, out float2 p1)
    {
        var direction = math.normalize(center);

        // Distance from camera to center of sphere
        var d = math.length(center);

        // Distance from camera to sphere horizon edge
        var l = math.sqrt(d * d - radius * radius);

        // Height of circle horizon
        var h = l * radius / d;

        // Center of circle horizon
        var c = direction * (l * h / radius);

        p0 = math.float2(float.MinValue, 1f);
        p1 = math.float2(float.MaxValue, 1f);

        // Handle clipping
        if (center.y - radius < near)
        {
            p0 = math.float2(center.x + clipRadius, near);
            p1 = math.float2(center.x - clipRadius, near);
        }

        // Circle horizon points
        var c0 = c + math.float2(-direction.y, direction.x) * h;
        if (square(d) >= square(radius) && c0.y >= near)
        {
            if (c0.x > p0.x) { p0 = c0; }
            if (c0.x < p1.x) { p1 = c0; }
        }

        var c1 = c + math.float2(direction.y, -direction.x) * h;
        if (square(d) >= square(radius) && c1.y >= near)
        {
            if (c1.x > p0.x) { p0 = c1; }
            if (c1.x < p1.x) { p1 = c1; }
        }
    }

    /// <summary>
    /// Calculates the horizon of a circle orthogonally projected to a plane as seen from the origin on the plane.
    /// </summary>
    /// <param name="center">The center of the circle projected onto the plane.</param>
    /// <param name="radius">The radius of the circle.</param>
    /// <param name="U">The major axis of the ellipse formed by the projection of the circle.</param>
    /// <param name="V">The minor axis of the ellipse formed by the projection of the circle.</param>
    /// <param name="uv1">The first horizon point expressed as factors of <paramref name="U"/> and <paramref name="V"/>.</param>
    /// <param name="uv2">The second horizon point expressed as factors of <paramref name="U"/> and <paramref name="V"/>.</param>
    static void GetProjectedCircleHorizon(float2 center, float radius, float2 U, float2 V, out float2 uv1, out float2 uv2)
    {
        // U is assumed to be constructed such that it is never 0, but V can be if the circle projects to a line segment.
        // In that case, the solution can be trivially found using U only.
        var vl = math.length(V);
        if (vl < 1e-6f)
        {
            uv1 = math.float2(radius, 0);
            uv2 = math.float2(-radius, 0);
        }
        else
        {
            var ul = math.length(U);
            var ulinv = math.rcp(ul);
            var vlinv = math.rcp(vl);

            // Normalize U and V in the plane.
            var u = U * ulinv;
            var v = V * vlinv;

            // Major and minor axis of the ellipse.
            var a = ul * radius;
            var b = vl * radius;

            // Project the camera position into a 2D coordinate system with the circle at (0, 0) and
            // the ellipse major and minor axes as the coordinate system axes. This allows us to use the standard
            // form of the ellipse equation, greatly simplifying the calculations.
            var cameraUV = math.float2(math.dot(-center, u), math.dot(-center, v));

            // Find the polar line of the camera position in the normalized UV coordinate system.
            var polar = math.float3(cameraUV.x / square(a), cameraUV.y / square(b), -1);
            var (t1, t2) = IntersectEllipseLine(a, b, polar);

            // Find Y by putting polar into line equation and solving. Denormalize by dividing by U and V lengths.
            uv1 = math.float2(t1 * ulinv, (-polar.x / polar.y * t1 - polar.z / polar.y) * vlinv);
            uv2 = math.float2(t2 * ulinv, (-polar.x / polar.y * t2 - polar.z / polar.y) * vlinv);
        }
    }

    static (float, float) IntersectEllipseLine(float a, float b, float3 line)
    {
        // The line is represented as a homogenous 2D line {u, v, w} such that ux + vy + w = 0.
        // The ellipse is represented by the implicit equation x^2/a^2 + y^2/b^2 = 1.
        // We solve the line equation for y:  y = (ux + w) / v
        // We then substitute this into the ellipse equation and expand and re-arrange a bit:
        //   x^2/a^2 + ((ux + w) / v)^2/b^2 = 1 =>
        //   x^2/a^2 + ((ux + w)^2 / v^2)/b^2 = 1 =>
        //   x^2/a^2 + (ux + w)^2/(v^2 b^2) = 1 =>
        //   x^2/a^2 + (u^2 x^2 + w^2 + 2 u x w)/(v^2 b^2) = 1 =>
        //   x^2/a^2 + x^2 u^2 / (v^2 b^2) + w^2/(v^2 b^2) + x 2 u w / (v^2 b^2) = 1 =>
        //   x^2 (1/a^2 + u^2 / (v^2 b^2)) + x 2 u w / (v^2 b^2) + w^2 / (v^2 b^2) - 1 = 0
        // We now have a quadratic equation with:
        //   a = 1/a^2 + u^2 / (v^2 b^2)
        //   b = 2 u w / (v^2 b^2)
        //   c = w^2 / (v^2 b^2) - 1
        var div = math.rcp(square(line.y) * square(b));
        var qa = 1f / square(a) + square(line.x) * div;
        var qb = 2f * line.x * line.z * div;
        var qc = square(line.z) * div - 1f;
        var sqrtD = math.sqrt(qb * qb - 4f * qa * qc);
        var x1 = (-qb + sqrtD) / (2f * qa);
        var x2 = (-qb - sqrtD) / (2f * qa);
        return (x1, x2);
    }

    /// <summary>
    /// Finds the two points of intersection of a 3D circle and the near plane.
    /// </summary>
    static bool GetCircleClipPoints(float3 circleCenter, float3 circleNormal, float circleRadius, float near, out float3 p0, out float3 p1)
    {
        // The intersection of two planes is a line where the direction is the cross product of the two plane normals.
        // In this case, it is the plane containing the circle, and the near plane.
        var lineDirection = math.normalize(math.cross(circleNormal, math.float3(0, 0, 1)));

        // Find a direction on the circle plane towards the nearest point on the intersection line.
        // It has to be perpendicular to the circle normal to be in the circle plane. The direction to the closest
        // point on a line is perpendicular to the line direction. Thus this is given by the cross product of the
        // line direction and the circle normal, as this gives us a vector that is perpendicular to both of those.
        var nearestDirection = math.cross(lineDirection, circleNormal);

        // Distance from circle center to the intersection line along `nearestDirection`.
        // This is done using a ray-plane intersection, where the plane is the near plane.
        // ({0, 0, near} - circleCenter) . {0, 0, 1} / (nearestDirection . {0, 0, 1})
        var distance = (near - circleCenter.z) / nearestDirection.z;

        // The point on the line nearest to the circle center when traveling only in the circle plane.
        var nearestPoint = circleCenter + nearestDirection * distance;

        // Any line through a circle makes a chord where the endpoints are the intersections with the circle.
        // The half length of the circle chord can be found by constructing a right triangle from three points:
        // (a) The circle center.
        // (b) The nearest point.
        // (c) A point that is on circle and the intersection line.
        // The hypotenuse is formed by (a) and (c) and will have length `circleRadius` as it is on the circle.
        // The known side if formed by (a) and (b), which we have already calculated the distance of in `distance`.
        // The unknown side formed by (b) and (c) is then found using Pythagoras.
        var chordHalfLength = math.sqrt(square(circleRadius) - square(distance));
        p0 = nearestPoint + lineDirection * chordHalfLength;
        p1 = nearestPoint - lineDirection * chordHalfLength;

        return math.abs(distance) <= circleRadius;
    }

    static float3 EvaluateNearConic(float near, float3 o, float3 d, float r, float3 u, float3 v, float theta)
    {
        var h = (near - o.z) / (d.z + r * u.z * math.cos(theta) + r * v.z * math.sin(theta));
        return math.float3(o.xy + h * (d.xy + r * u.xy * math.cos(theta) + r * v.xy * math.sin(theta)), near);
    }

    // o, d, u and v are expected to contain {x or y, z}. I.e. pass in x values to find tangents where x' = 0
    // Returns the two theta values as a float2.
    static float2 FindNearConicTangentTheta(float2 o, float2 d, float r, float2 u, float2 v)
    {
        var sqrt = math.sqrt(square(d.x) * square(u.y) + square(d.x) * square(v.y) - 2f * d.x * d.y * u.x * u.y - 2f * d.x * d.y * v.x * v.y + square(d.y) * square(u.x) + square(d.y) * square(v.x) - square(r) * square(u.x) * square(v.y) + 2f * square(r) * u.x * u.y * v.x * v.y - square(r) * square(u.y) * square(v.x));
        var denom = d.x * v.y - d.y * v.x - r * u.x * v.y + r * u.y * v.x;
        return 2 * math.atan((-d.x * u.y + d.y * u.x + math.float2(1, -1) * sqrt) / denom);
    }

    static void GetConeSideTangentPoints(float3 vertex, float3 axis, float cosHalfAngle, float circleRadius, float coneHeight, float range, float3 circleU, float3 circleV, out float3 l1, out float3 l2)
    {
        l1 = l2 = 0;

        if (math.dot(math.normalize(-vertex), axis) >= cosHalfAngle)
        {
            return;
        }

        var d = -math.dot(vertex, axis);
        // If d is zero, this leads to a numerical instability in the code later on. This is why we make the value
        // an epsilon if it is zero.
        if (d == 0f) d = 1e-6f;
        var sign = d < 0 ? -1f : 1f;
        // sign *= vertex.z < 0 ? -1f : 1f;
        // `origin` is the center of the circular slice we're about to calculate at distance `d` from the `vertex`.
        var origin = vertex + axis * d;
        // Get the radius of the circular slice of the cone at the `origin`.
        var radius = math.abs(d) * circleRadius / coneHeight;
        // `circleU` and `circleV` are the two vectors perpendicular to the cone's axis. `cameraUV` is thus the
        // position of the camera projected onto the plane of the circular slice. This basically creates a new
        // 2D coordinate space, with (0, 0) located at the center of the circular slice, which why this variable
        // is called `origin`.
        var cameraUV = math.float2(math.dot(circleU, -origin), math.dot(circleV, -origin));
        // Use homogeneous coordinates to find the tangents.
        var polar = math.float3(cameraUV, -square(radius));
        var p1 = math.float2(-1, -polar.x / polar.y * (-1) - polar.z / polar.y);
        var p2 = math.float2(1, -polar.x / polar.y * 1 - polar.z / polar.y);
        var lineDirection = math.normalize(p2 - p1);
        var lineNormal = math.float2(lineDirection.y, -lineDirection.x);
        var distToLine = math.dot(p1, lineNormal);
        var lineCenter = lineNormal * distToLine;
        var l = math.sqrt(radius * radius - distToLine * distToLine);
        var x1UV = lineCenter + l * lineDirection;
        var x2UV = lineCenter - l * lineDirection;
        var dir1 = math.normalize((origin + x1UV.x * circleU + x1UV.y * circleV) - vertex) * sign;
        var dir2 = math.normalize((origin + x2UV.x * circleU + x2UV.y * circleV) - vertex) * sign;
        l1 = dir1 * range;
        l2 = dir2 * range;
    }

    static float2 FindNearConicYTheta(float near, float3 o, float3 d, float r, float3 u, float3 v, float y)
    {
        var sqrt = math.sqrt(-square(d.y) * square(o.z) + 2 * square(d.y) * o.z * near - square(d.y) * square(near) + 2 * d.y * d.z * o.y * o.z - 2 * d.y * d.z * o.y * near - 2 * d.y * d.z * o.z * y + 2 * d.y * d.z * y * near - square(d.z) * square(o.y) + 2 * square(d.z) * o.y * y - square(d.z) * square(y) + square(o.y) * square(r) * square(u.z) + square(o.y) * square(r) * square(v.z) - 2 * o.y * o.z * square(r) * u.y * u.z - 2 * o.y * o.z * square(r) * v.y * v.z - 2 * o.y * y * square(r) * square(u.z) - 2 * o.y * y * square(r) * square(v.z) + 2 * o.y * square(r) * u.y * u.z * near + 2 * o.y * square(r) * v.y * v.z * near + square(o.z) * square(r) * square(u.y) + square(o.z) * square(r) * square(v.y) + 2 * o.z * y * square(r) * u.y * u.z + 2 * o.z * y * square(r) * v.y * v.z - 2 * o.z * square(r) * square(u.y) * near - 2 * o.z * square(r) * square(v.y) * near + square(y) * square(r) * square(u.z) + square(y) * square(r) * square(v.z) - 2 * y * square(r) * u.y * u.z * near - 2 * y * square(r) * v.y * v.z * near + square(r) * square(u.y) * square(near) + square(r) * square(v.y) * square(near));
        var denom = d.y * o.z - d.y * near - d.z * o.y + d.z * y + o.y * r * u.z - o.z * r * u.y - y * r * u.z + r * u.y * near;
        return 2 * math.atan((r * (o.y * v.z - o.z * v.y - y * v.z + v.y * near) + math.float2(1, -1) * sqrt) / denom);
    }

    static void GetSphereYPlaneHorizon(float3 center, float sphereRadius, float near, float clipRadius, float y, out float3 left, out float3 right)
    {
        // Note: The y-plane is the plane that is determined by `y` in that it contains the vector (1, 0, 0)
        // and goes through the points (0, y, 1) and (0, 0, 0). This would become a straight line in screen-space, and so it
        // represents the boundary between two rows of tiles.

        // Near-plane clipping - will get overwritten if no clipping is needed.
        // `y` is given for the view plane (Z=1), scale it so that it is on the near plane instead.
        var yNear = y * near;
        // Find the two points of intersection between the clip circle of the sphere and the y-plane.
        // Found using Pythagoras with a right triangle formed by three points:
        // (a) center of the clip circle
        // (b) a point straight above the clip circle center on the y-plane
        // (c) a point that is both on the circle and the y-plane (this is the point we want to find in the end)
        // The hypotenuse is formed by (a) and (c) with length equal to the clip radius. The known side is
        // formed by (a) and (b) and is simply the distance from the center to the y-plane along the y-axis.
        // The remaining side gives us the x-displacement needed to find the intersection points.
        var clipHalfWidth = math.sqrt(square(clipRadius) - square(yNear - center.y));
        left = math.float3(center.x - clipHalfWidth, yNear, near);
        right = math.float3(center.x + clipHalfWidth, yNear, near);

        // Basis vectors in the y-plane for being able to parameterize the plane.
        var planeU = math.normalize(math.float3(0, y, 1));
        var planeV = math.float3(1, 0, 0);

        // Calculate the normal of the y-plane. Found from: (0, y, 1) × (1, 0, 0) = (0, 1, -y)
        // This is used to represent the plane along with the origin, which is just 0 and thus doesn't show up
        // in the calculations.
        var normal = math.normalize(math.float3(0, 1, -y));

        // We want to first find the circle from the intersection of the y-plane and the sphere.

        // The shortest distance from the sphere center and the y-plane. The sign determines which side of the plane
        // the center is on.
        var signedDistance = math.dot(normal, center);

        // Unsigned shortest distance from the sphere center to the plane.
        var distanceToPlane = math.abs(signedDistance);

        // The center of the intersection circle in the y-plane, which is the point on the plane closest to the
        // sphere center. I.e. this is at `distanceToPlane` from the center.
        var centerOnPlane = math.float2(math.dot(center, planeU), math.dot(center, planeV));

        // Distance from origin to the circle center.
        var distanceInPlane = math.length(centerOnPlane);

        // Direction from origin to the circle center.
        var directionPS = centerOnPlane / distanceInPlane;

        // Calculate the radius of the circle using Pythagoras. We know that any point on the circle is a point on
        // the sphere. Thus we can construct a triangle with the sphere center, circle center, and a point on the
        // circle. We then want to find its distance to the circle center, as that will be equal to the radius. As
        // the point is on the sphere, it must be `sphereRadius` from the sphere center, forming the hypotenuse. The
        // other side is between the sphere and circle centers, which we've already calculated to be
        // `distanceToPlane`.
        var circleRadius = math.sqrt(square(sphereRadius) - square(distanceToPlane));

        // Now that we have the circle, we can find the horizon points. Since we've parametrized the plane, we can
        // just do this in 2D.

        // Any of these conditions will yield NaN due to negative square roots. They are signs that clipping is needed,
        // so we fallback on the already calculated values in that case.
        if (square(distanceToPlane) <= square(sphereRadius) && square(circleRadius) <= square(distanceInPlane))
        {
            // Distance from origin to circle horizon edge.
            var l = math.sqrt(square(distanceInPlane) - square(circleRadius));

            // Height of circle horizon.
            var h = l * circleRadius / distanceInPlane;

            // Center of circle horizon.
            var c = directionPS * (l * h / circleRadius);

            // Calculate the horizon points in the plane.
            var leftOnPlane = c + math.float2(directionPS.y, -directionPS.x) * h;
            var rightOnPlane = c + math.float2(-directionPS.y, directionPS.x) * h;

            // Transform horizon points to view space and use if not clipped.
            var leftCandidate = leftOnPlane.x * planeU + leftOnPlane.y * planeV;
            if (leftCandidate.z >= near) left = leftCandidate;

            var rightCandidate = rightOnPlane.x * planeU + rightOnPlane.y * planeV;
            if (rightCandidate.z >= near) right = rightCandidate;
        }
    }

    static bool IntersectCircleYPlane(float y, float3 circleCenter, float3 circleNormal, float3 circleU, float3 circleV, float circleRadius, out float3 p1, out float3 p2)
    {
        p1 = p2 = 0;

        // Intersecting a circle with a plane yields 2 points, or the whole circle if the plane and the plane of the
        // circle are the same, or nothing if the planes are parallel but offset. We're only interested in the first
        // case. Our other tests will catch the other cases.

        // The two points will be on the line of intersection of the two planes. Thus we first have to find that line.

        // Shoot 2 rays along the y-plane and intersect the circle plane. We then transform them into the circle
        // plane, so that we can work in 2D.
        var CdotN = math.dot(circleCenter, circleNormal);
        var h1v = math.float3(1, y, 1) * CdotN / math.dot(math.float3(1, y, 1), circleNormal) - circleCenter;
        var h1 = math.float2(math.dot(h1v, circleU), math.dot(h1v, circleV));
        var h2v = math.float3(-1, y, 1) * CdotN / math.dot(math.float3(-1, y, 1), circleNormal) - circleCenter;
        var h2 = math.float2(math.dot(h2v, circleU), math.dot(h2v, circleV));

        var lineDirection = math.normalize(h2 - h1);
        // We now have the direction of the line, and would like to find the point on it that is closest to the
        // circle center. A line in 2D is similar to a plane in 3D. So we can calculate a normal, which is just a
        // perpendicular/orthogonal direction, and then take the dot product to find the distance. This is similar
        // to when calculating the d-term for a plane in 3D, which is also just calculating the closest distance
        // from the origin to the plane.
        var lineNormal = math.float2(lineDirection.y, -lineDirection.x);
        var distToLine = math.dot(h1, lineNormal);
        // We can then get that point on the line by following our normal with the distance we just calculated.
        var lineCenter = lineNormal * distToLine;

        // Avoid negative square roots, as this means we've hit one of the cases that we do not care about.
        if (distToLine > circleRadius) return false;

        // What's left now is to intersect the line with the circle. We can do so with Pythagoras. Our triangle
        // is made up of `lineCenter`, the circle center and one of the intersection points.
        // We know the distance from `lineCenter` to the circle center (`distToLine`), and the distance from
        // the circle center to one of the intersection points must be the circle radius, as it lies on the
        // circle, forming the hypotenuse.
        var l = math.sqrt(circleRadius * circleRadius - distToLine * distToLine);

        // What we found above is the distance from `lineCenter` to each of the intersection points. So we just
        // scrub along the line in both directions using the found distance, and then transform back into view
        // space.
        var x1 = lineCenter + l * lineDirection;
        var x2 = lineCenter - l * lineDirection;
        p1 = circleCenter + x1.x * circleU + x1.y * circleV;
        p2 = circleCenter + x2.x * circleU + x2.y * circleV;

        return true;
    }
}

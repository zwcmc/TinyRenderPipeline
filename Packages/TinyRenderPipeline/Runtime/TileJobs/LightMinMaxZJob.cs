using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

[BurstCompile]
struct LightMinMaxZJob : IJobFor
{
    public float4x4 worldToView;

    [ReadOnly]
    public NativeArray<VisibleLight> lights;

    public NativeArray<float2> minMaxZs;

    public void Execute(int index)
    {
        var lightIndex = index % lights.Length;
        var light = lights[lightIndex];

        // 此光源的本地空间到世界空间矩阵
        var lightToWorld = (float4x4)light.localToWorldMatrix;
        // 此光源原点的世界坐标
        var originWS = lightToWorld.c3.xyz;
        // 此光源原点在观察空间的坐标
        var originVS = math.mul(worldToView, math.float4(originWS, 1f)).xyz;

        // 因为 Unity 在观察空间使用的是右手坐标系，-z 方向指向的是物体前方，所以 z 的值都是负的，所以在这里取反，最终存储的都是正值
        originVS.z *= -1;

        // 点光源在观察空间下的最大深度和最小深度
        var minMax = math.float2(originVS.z - light.range, originVS.z + light.range);

        // 如果是聚光灯光源，聚光灯光源和点光源不一样，点光源的范围是一个球形，而聚光灯光源的范围是一个锥体
        // 锥体的包围盒计算使用了 https://iquilezles.org/articles/diskbbox/ 中的 ConeAABB 算法
        if (light.lightType == LightType.Spot)
        {
            // 锥体角度的一半
            var angleA = math.radians(light.spotAngle) * 0.5f;
            float cosAngleA = math.cos(angleA);

            // 聚光灯锥体的高度
            float coneHeight = light.range * cosAngleA;

            // 聚光灯方向
            float3 spotDirectionWS = lightToWorld.c2.xyz;

            // 计算出聚光灯锥体底面圆的中心在观察空间的坐标
            var endPointWS = originWS + spotDirectionWS * coneHeight;
            var endPointVS = math.mul(worldToView, math.float4(endPointWS, 1f)).xyz;
            endPointVS.z *= -1;

            // 聚光灯锥体底面圆的半径
            var angleB = math.PI * 0.5f - angleA;
            var coneRadius = light.range * cosAngleA * math.sin(angleA) / math.sin(angleB);

            // 原点到底面圆圆心的向量 a
            var a = endPointVS - originVS;
            // 求在底面圆上，圆心到圆上的单位 z 分量
            // 这个公式有所简化，原本应该是：
            // 首先，归一化向量 b：  b = a / sqrt(dot(a, a));
            // 根据原文中的算法，在锥体中，偏移的单位向量是：e = sqrt(1.0 - b * b)，也就是 e = sqrt(1.0 - (a / sqrt(dot(a, a)) * (a / sqrt(dot(a, a)));
            // 最终简化成：e = sqrt(1.0 - a * a / dot(a, a));
            // 而在这里只需要 z 方向的分量，所以只取 a.z 就可以
            var e = math.sqrt(1.0f - a.z * a.z / math.dot(a, a));

            // `-a.z` and `a.z` is `dot(a, {0, 0, -1}).z` and `dot(a, {0, 0, 1}).z` optimized
            // `cosAngleA` is multiplied by `coneHeight` to avoid normalizing `a`, which we know has length `coneHeight`

            // a 是原点到底面圆圆心的向量，coneHeight 是原点到底面圆圆心距离，向量 a 的长度就是 coneHeight；
            // 其中 -a.z 是简化计算：dot(a, (0, 0, -1)).z，而 a.z 是简化计算 dot(a, (0, 0, 1)).z，(`-a.z` and `a.z` is `dot(a, {0, 0, -1}).z` and `dot(a, {0, 0, 1}).z` optimized)；
            // 可以理解为 -a.z 是向量 a 在 负 z 方向 (0, 0, -1) 上的投影的长度；
            // 而 a.z 是向量 a 在正 z 方向 (0, 0, 1) 上的投影的长度；
            // 因为计算都是在观察空间，所以正 z 方向就是相机的面对方向，负 z 方向可以理解为与相机面对方向相反的方向；

            // 当 (-a.z < coneHeight * cosAngleA) = true 时，可以理解为：
            // 聚光灯锥体原点到底面圆圆心的向量在相机面对方向相反的方向上的投影长度小于原点到底面圆圆心的向量的长度，此时需要更新的是最小深度，从光源原点深度和底面圆心深度减去偏移半径深度中取最小；

            // 当 (a.z < coneHeight * cosAngleA) = true 时，理解为：
            // 原点到底面圆圆心的向量在相机面对方向上的投影长度小于原点到底面圆圆心的向量的长度，这时需要更新的时最大深度，从光源原点深度和底面圆心深度加上偏移半径深度中取最大；

            // 其中，coneHeight 乘了一个 cosAngleA 是为了防止归一化向量 a，(`cosAngleA` is multiplied by `coneHeight` to avoid normalizing `a`, which we know has length `coneHeight`)
            if (-a.z < coneHeight * cosAngleA)
                minMax.x = math.min(originVS.z, endPointVS.z - e * coneRadius);
            if (a.z < coneHeight * cosAngleA)
                minMax.y = math.max(originVS.z, endPointVS.z + e * coneRadius);
        }

        // 最后，把最小值和最大值限制在大于等于 0，小于 0 就是在相机之外了
        minMax.x = math.max(minMax.x, 0);
        minMax.y = math.max(minMax.y, 0);

        minMaxZs[index] = minMax;
    }
}

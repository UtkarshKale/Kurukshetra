using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Chaukanna.Scenario
{
    // Level of detail of one agent this frame.
    public static class CrowdLod
    {
        public const byte None = 0;     // not alive, outside the view, beyond the sensor range or too small to draw
        public const byte Dot = 1;      // 0.3 to 6 px: a tiny procedural silhouette
        public const byte Model = 2;    // over 6 px: the class model, part by part
    }

    // Position, heading, view test and projected size of every agent, in parallel.
    [BurstCompile]
    public struct CrowdUpdateJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<CrowdAgent> agents;
        [ReadOnly] public NativeArray<byte> removed;        // 1 = promoted to a real contact: never drawn or picked again
        [ReadOnly] public NativeArray<float> heights;
        public float2 gridOrigin;
        public float invCell;
        public int gridN;
        public float time;
        public float3 camPos;
        public float4x4 viewProj;                           // projection * view, OpenGL convention
        public float pxPerMetreAtOne;                       // screen height in px / (2 tan(vfov / 2)): px = size * this / distance
        public float maxRange, minPx, modelPx;

        [WriteOnly] public NativeArray<float3> pos;
        [WriteOnly] public NativeArray<float> yaw;
        [WriteOnly] public NativeArray<float> px;
        [WriteOnly] public NativeArray<byte> lod;
        [WriteOnly] public NativeArray<byte> alive;

        public void Execute(int i)
        {
            CrowdAgent a = agents[i];
            if (removed[i] != 0 || !CrowdMath.Alive(a, time))
            {
                alive[i] = 0; lod[i] = CrowdLod.None; px[i] = 0f; pos[i] = float3.zero; yaw[i] = 0f;
                return;
            }
            alive[i] = 1;
            float3 p = CrowdMath.Pos(a, time);
            float y = CrowdMath.WorldY(p.y, CrowdMath.Ground(heights, gridOrigin, invCell, gridN, p.x, p.z));
            float3 w = new float3(p.x, y, p.z);
            pos[i] = w;
            yaw[i] = CrowdMath.Yaw(a, time);
            float dist = math.distance(w, camPos);
            float size = a.size * pxPerMetreAtOne / math.max(dist, 0.01f);
            px[i] = size;
            byte l = CrowdLod.None;
            if (dist <= maxRange && size >= minPx)
            {
                float4 clip = math.mul(viewProj, new float4(w, 1f));
                float m = clip.w * 1.05f;                    // a little margin so nothing pops at the edge
                if (clip.w > 0f && math.abs(clip.x) <= m && math.abs(clip.y) <= m) l = size > modelPx ? CrowdLod.Model : CrowdLod.Dot;
            }
            lod[i] = l;
        }
    }

    // Packs the drawable agents into one matrix list per bucket (a counting sort, so nothing is allocated per frame).
    // Buckets: the first three are the tiny silhouettes, then one per model part. Matrix of a part = agent TRS * part local.
    [BurstCompile]
    public struct CrowdBucketJob : IJob
    {
        [ReadOnly] public NativeArray<CrowdAgent> agents;
        [ReadOnly] public NativeArray<byte> lod;
        [ReadOnly] public NativeArray<float3> pos;
        [ReadOnly] public NativeArray<float> yaw;
        [ReadOnly] public NativeArray<byte> alive;
        [ReadOnly] public NativeArray<int> modelFirst;      // [cls * MaxVariants + variant] -> first part bucket, -1 = no model
        [ReadOnly] public NativeArray<int> modelParts;      // ...and how many parts it has
        [ReadOnly] public NativeArray<float4x4> partLocal;  // per bucket (identity for the silhouettes)
        [ReadOnly] public NativeArray<int> dotBucket;       // [cls] -> silhouette bucket
        public int bucketCount;
        public NativeArray<int> counts, starts, fill;
        [WriteOnly] public NativeArray<float4x4> matrices;
        public NativeArray<int> stats;                      // 0 alive, 1 model-level agents, 2 dot-level agents

        public void Execute()
        {
            for (int b = 0; b < bucketCount; b++) { counts[b] = 0; fill[b] = 0; }
            int nAlive = 0, nModel = 0, nDot = 0;
            for (int i = 0; i < agents.Length; i++)
            {
                if (alive[i] != 0) nAlive++;
                byte l = lod[i];
                if (l == CrowdLod.None) continue;
                CrowdAgent a = agents[i];
                int key = a.cls * CrowdGenerator.MaxVariants + a.variant;
                if (l == CrowdLod.Model && modelFirst[key] >= 0 && modelParts[key] > 0)
                {
                    nModel++;
                    int f = modelFirst[key], n = modelParts[key];
                    for (int k = 0; k < n; k++) counts[f + k]++;
                }
                else { nDot++; counts[dotBucket[a.cls]]++; }
            }
            int run = 0;
            for (int b = 0; b < bucketCount; b++) { starts[b] = run; run += counts[b]; }
            for (int i = 0; i < agents.Length; i++)
            {
                byte l = lod[i];
                if (l == CrowdLod.None) continue;
                CrowdAgent a = agents[i];
                float4x4 world = float4x4.TRS(pos[i], quaternion.RotateY(yaw[i]), new float3(a.size));
                int key = a.cls * CrowdGenerator.MaxVariants + a.variant;
                if (l == CrowdLod.Model && modelFirst[key] >= 0 && modelParts[key] > 0)
                {
                    int f = modelFirst[key], n = modelParts[key];
                    for (int k = 0; k < n; k++)
                    {
                        int b = f + k;
                        matrices[starts[b] + fill[b]++] = math.mul(world, partLocal[b]);
                    }
                }
                else
                {
                    int b = dotBucket[a.cls];
                    matrices[starts[b] + fill[b]++] = world;
                }
            }
            stats[0] = nAlive; stats[1] = nModel; stats[2] = nDot;
        }
    }
}

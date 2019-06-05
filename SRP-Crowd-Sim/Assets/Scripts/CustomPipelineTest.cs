using UnityEngine;
using UnityEngine.Rendering;
//using UnityEngine.Experimental.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Pipeline Test")]
public class CustomPipelineTest : RenderPipelineAsset {

    [SerializeField] bool useDynamicBatching;
    [SerializeField] bool useGpuInstancing;
    protected override RenderPipeline CreatePipeline()
    {
        return new MyPipelineInstance(useDynamicBatching, useGpuInstancing);
    }

}
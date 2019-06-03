using UnityEngine;
using UnityEngine.Rendering;
//using UnityEngine.Experimental.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Pipeline Test")]
public class CustomPipelineTest : RenderPipelineAsset {
    protected override RenderPipeline CreatePipeline()
    {
        return new MyPipelineInstance();
    }
}
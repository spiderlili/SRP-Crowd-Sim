using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Custom Pipeline Test")]
public class CustomPipelineTest : RenderPipelineAsset {
    protected override RenderPipeline CreatePipeline()
    {
        return null;
    }

}
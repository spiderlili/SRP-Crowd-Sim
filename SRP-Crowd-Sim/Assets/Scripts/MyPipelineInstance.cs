using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

public class MyPipelineInstance : RenderPipeline
{
    protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        //base.Render(renderContext, cameras);
        renderContext.DrawSkybox(cameras[0]);
        renderContext.Submit();
    }

    /*
    protected override RenderPipeline InternalCreatePipeline()
    {
        return new MyPipeline();
    }*/

}

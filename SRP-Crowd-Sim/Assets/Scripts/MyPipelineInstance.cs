using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using RenderPipeline = UnityEngine.Rendering.RenderPipeline;

public class MyPipelineInstance : RenderPipeline
{
    protected CullingResults cullingResults;

    //render context = a facade for native code, cameras = all cameras that need to be rendered
    protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        //base.Render(renderContext, cameras);
        foreach (var camera in cameras)
        {
            Render(renderContext, camera);
        }
    }

    //an alt Render method that acts on a single camera => draw the skybox and submit per camera
    void Render(ScriptableRenderContext context, Camera camera)
    {
        //culling step
        ScriptableCullingParameters cullingParameters;
        // TryGetCullingParameters return false if it failed to create valid params
        if (!camera.TryGetCullingParameters(out cullingParameters))
        {
            return;
        }
        // Inject world space UI into scene view
#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif

        // Sends culling instructions to context
        cullingResults = context.Cull(ref cullingParameters);
        //CullingResults.GetCullingParameters(camera, out cullingParameters); //redundant api

        //rendering step: to correctly render the skybox and scene: set up view-projection matrix 
        //combine view matrix (camera's position and orientation) w projection matrix(camera's perspective/orthographic projection)
        context.SetupCameraProperties(camera); // Sets up camera specific global shader params

        //clear the depth data, ignore colour data, use Color.clear as the clear colour
        //use the camera's name as the command buffer's name so it's easy to read in the debugger
        var buffer = new CommandBuffer
        {
            name = camera.name
        };

        //testing
        //buffer.ClearRenderTarget(true, false, Color.clear);
        //context.ExecuteCommandBuffer(buffer);
        //buffer.Release();

        //use config per camera to determine what gets cleared in the render target, via its clear flags and background colour
        CameraClearFlags clearFlags = camera.clearFlags; // clear the render target with command buffers
        buffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

        context.DrawSkybox(camera);

        context.Submit();
    }
}

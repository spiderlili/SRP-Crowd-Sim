using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using RenderPipeline = UnityEngine.Rendering.RenderPipeline;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipelineInstance : RenderPipeline
{
    protected CullingResults cullingResults;
    Material errorMaterial;

    CommandBuffer cameraBuffer = new CommandBuffer
    {
        name = "Render Camera"
    };

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

        // Inject world space UI into scene view - must be done before culling. Only include the code when compiling for editor
#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif

        // Sends culling instructions to context
        cullingResults = context.Cull(ref cullingParameters);
        
        //CullResults cull = CullResults.Cull(ref cullingParameters, context);
        //CullingResults.GetCullingParameters(camera, out cullingParameters); //redundant api

        //rendering step: to correctly render the skybox and scene: set up view-projection matrix 
        //combine view matrix (camera's position and orientation) w projection matrix(camera's perspective/orthographic projection)
        context.SetupCameraProperties(camera); // Sets up camera specific global shader params

        //clear the depth data, ignore colour data, use Color.clear as the clear colour
        //use the camera's name as the command buffer's name so it's easy to read in the debugger
        //disable to save performance - source of continuous memory alloc: try always name cmd buffer Render Camera
        //testing: can disable var buffer part
        /* var buffer = new CommandBuffer
        {
           name = camera.name          
        }; */

       
        cameraBuffer.ClearRenderTarget(true, false, Color.clear);
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();
        //buffer.Release();

        //use config per camera to determine what gets cleared in the render target, via its clear flags and background colour
        CameraClearFlags clearFlags = camera.clearFlags; // clear the render target with command buffers
        cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

        //drawing step: draw visible shapes, instruct unity to sort the renderers by distance from front to back
        var drawSettings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), new SortingSettings(camera));
        var sortingSettings = new SortingSettings(camera);
        sortingSettings.criteria = SortingCriteria.CommonOpaque;

        var filterSettings = new FilteringSettings(RenderQueueRange.opaque);


        context.DrawRenderers(cullingResults, ref drawSettings, ref filterSettings);

        context.DrawSkybox(camera);
        //revere draw order from back tofront
        sortingSettings.criteria = SortingCriteria.CommonTransparent;

        //change the queue range to transparent after rendering skybox and render again
        filterSettings.renderQueueRange = RenderQueueRange.transparent;
        context.DrawRenderers(cullingResults, ref drawSettings, ref filterSettings);

        //render the default pipeline, invoke it at the end after drawing the transparent shapes to visualise error shader     
        DrawDefaultPipeline(context, camera);

        context.Submit();
    }

    //only invoke this func in the editor, not in the build
    [Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_EDITOR")]
    void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera)
    {
        var drawSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), new SortingSettings(camera));

        //add multiple passes to the draw settings so built in shaders that use an unsupported material can clearly show up incorrect
        drawSettings.SetShaderPassName(1, new ShaderTagId("PrepassBase"));
        drawSettings.SetShaderPassName(2, new ShaderTagId("Always"));
        drawSettings.SetShaderPassName(3, new ShaderTagId("Vertex"));
        drawSettings.SetShaderPassName(4, new ShaderTagId("VertexLMRGBM"));
        drawSettings.SetShaderPassName(5, new ShaderTagId("VertexLM"));

        drawSettings.overrideMaterial = errorMaterial;
        var filterSettings = new FilteringSettings(RenderQueueRange.opaque);
        context.DrawRenderers(cullingResults, ref drawSettings, ref filterSettings);

        if(errorMaterial == null)
        {
            Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
            errorMaterial = new Material(errorShader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
        }
    }
}

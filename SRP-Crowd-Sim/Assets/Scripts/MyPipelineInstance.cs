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

    bool useDynamicBatching;
    bool useGPUInstancing;

    const int maxVisibleLights = 16;

    //fill the buffer - shader IDs are constant per session - pass light data to gpu
    static int visibleLightColorsId =
    Shader.PropertyToID("_VisibleLightColors");
    static int visibleLightDirectionsOrPositionsId =
        Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    static int visibleLightAttenuationsId =
        Shader.PropertyToID("_VisibleLightAttenuations");
    static int visibleLightSpotDirectionsId =
        Shader.PropertyToID("_VisibleLightSpotDirections");
    static int lightIndicesOffsetAndCountID =
        Shader.PropertyToID("unity_LightIndicesOffsetAndCount");

    Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
    Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
    Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
    Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];

    CommandBuffer cameraBuffer = new CommandBuffer
    {
        name = "Render Camera"
    };
    
    public MyPipelineInstance(bool batch, bool instancing)
    {
        useDynamicBatching = batch;
        useGPUInstancing = instancing;
    }

    
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

        ConfigureLights();

        cameraBuffer.BeginSample("Render Camera");

        //copy the colour arrays to gpu by SetGlobalVectorArray on a cmd buffer then executing it
        cameraBuffer.SetGlobalVectorArray(
            visibleLightColorsId, visibleLightColors
        );
        cameraBuffer.SetGlobalVectorArray(
            visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
        );
        cameraBuffer.SetGlobalVectorArray(
            visibleLightAttenuationsId, visibleLightAttenuations
        );
        cameraBuffer.SetGlobalVectorArray(
            visibleLightSpotDirectionsId, visibleLightSpotDirections
        );
        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        //use config per camera to determine what gets cleared in the render target, via its clear flags and background colour
        CameraClearFlags clearFlags = camera.clearFlags; // clear the render target with command buffers
        cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

        context.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        //drawing step: draw visible shapes, instruct unity to sort the renderers by distance from front to back
        var drawSettings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), new SortingSettings(camera) {
        });
        drawSettings.enableDynamicBatching = useDynamicBatching;
        drawSettings.enableInstancing = useGPUInstancing;


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

    //config lights: figure out which lights are visible and have it loop through the list
    void ConfigureLights()
    {
        for (int i = 0; i < cullingResults.visibleLights.Length; i++)
        {
            if (i == maxVisibleLights)
            {
                break;
            }
            VisibleLight light = cullingResults.visibleLights[i];
            visibleLightColors[i] = light.finalColor;
            Vector4 attenuation = Vector4.zero;
            attenuation.w = 1f;

            if (light.lightType == LightType.Directional)
            {
                Vector4 v = light.localToWorldMatrix.GetColumn(2);
                v.x = -v.x;
                v.y = -v.y;
                v.z = -v.z;
                visibleLightDirectionsOrPositions[i] = v;
            }
            else
            {
                visibleLightDirectionsOrPositions[i] =
                    light.localToWorldMatrix.GetColumn(3);
                attenuation.x = 1f /
                    Mathf.Max(light.range * light.range, 0.00001f);

                if (light.lightType == LightType.Spot)
                {
                    Vector4 v = light.localToWorldMatrix.GetColumn(2);
                    v.x = -v.x;
                    v.y = -v.y;
                    v.z = -v.z;
                    visibleLightSpotDirections[i] = v;

                    float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                    float outerCos = Mathf.Cos(outerRad);
                    float outerTan = Mathf.Tan(outerRad);
                    float innerCos =
                        Mathf.Cos(Mathf.Atan((46f / 64f) * outerTan));
                    float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
                    attenuation.z = 1f / angleRange;
                    attenuation.w = -outerCos * attenuation.z;
                }
            }

            visibleLightAttenuations[i] = attenuation;
            ++i;
            if (i >= maxVisibleLights)
            {
                break;
            }
        }

        for (int i = 0; i < maxVisibleLights; i++)
        {
            visibleLightColors[i] = Color.clear;
        }
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

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using RenderPipeline = UnityEngine.Rendering.RenderPipeline;
using Conditional = System.Diagnostics.ConditionalAttribute;

public class MyPipelineInstance : RenderPipeline
{
    private static readonly ShaderTagId srp_PassName = new ShaderTagId("SRPDefaultUnlit"); //The shader pass tag just for SRP0601

    protected CullingResults cullingResults;
    Material errorMaterial;

    bool useDynamicBatching;
    bool useGPUInstancing;

    const int maxVisibleLightsCount = 16;

    //fill the buffer - shader IDs are constant per session - pass light data to gpu for realtime lights
    static int visibleLightColorsId =
    Shader.PropertyToID("_VisibleLightColors");
    static int visibleLightDataID = Shader.PropertyToID("_VisibleLightDataArray");
    static int visibleLightDirectionsId =
        Shader.PropertyToID("_VisibleLightDirections");

    static int visibleLightDirectionsOrPositionsId =
        Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
    static int visibleLightAttenuationsId =
        Shader.PropertyToID("_VisibleLightAttenuations");
    static int visibleLightSpotDirectionsId =
        Shader.PropertyToID("_VisibleLightSpotDirections"); //spot light support
    static int lightIndicesOffsetAndCountID =
        Shader.PropertyToID("unity_LightIndicesOffsetAndCount");


    Vector4[] visibleLightColors = new Vector4[maxVisibleLightsCount];
    Vector4[] visibleLightData = new Vector4[maxVisibleLightsCount];
    Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLightsCount];
    Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLightsCount];
    Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLightsCount];

    CommandBuffer cameraBuffer = new CommandBuffer
    {
        name = "Render Camera"
    };
    
    public MyPipelineInstance(bool batch, bool instancing)
    {
        GraphicsSettings.lightsUseLinearIntensity = true; //Override default gamma behaviour
        useDynamicBatching = batch;
        useGPUInstancing = instancing;
    }

    
    //render context = a facade for native code, cameras = all cameras that need to be rendered
    //an alt Render method that acts on a single camera => draw the skybox and submit per camera
    protected override void Render(ScriptableRenderContext renderContext, Camera[] cameras)
    {
        BeginFrameRendering(cameras);

        foreach (var camera in cameras)
        {
            //Render(renderContext, camera);
            BeginCameraRendering(camera);
        
        //culling step
        ScriptableCullingParameters cullingParameters;
        // TryGetCullingParameters return false if it failed to create valid params
        if (!camera.TryGetCullingParameters(out cullingParameters))
        {
            continue;
        }

        // Inject world space UI into scene view - must be done before culling. Only include the code when compiling for editor
#if UNITY_EDITOR
        if (camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif

        // Sends culling instructions to context
        cullingResults = renderContext.Cull(ref cullingParameters);

        //rendering step: to correctly render the skybox and scene: set up view-projection matrix 
        //combine view matrix (camera's position and orientation) w projection matrix(camera's perspective/orthographic projection)
        renderContext.SetupCameraProperties(camera); // Sets up camera specific global shader params

        //Get the setting from camera component
        bool drawSkyBox = camera.clearFlags == CameraClearFlags.Skybox ? true : false;
        bool clearDepth = camera.clearFlags == CameraClearFlags.Nothing ? false : true;
        bool clearColor = camera.clearFlags == CameraClearFlags.Color ? true : false;

            //clear the depth data, ignore colour data, use Color.clear as the clear colour
            //use the camera's name as the command buffer's name so it's easy to read in the debugger
            //disable to save performance - source of continuous memory alloc: try always name cmd buffer Render Camera
            //testing: can disable var buffer part
            /* var buffer = new CommandBuffer
            {
               name = camera.name          
            }; */

        var cameraClearFlagCmd = CommandBufferPool.Get("Clear");
        cameraClearFlagCmd.ClearRenderTarget(clearDepth, clearColor, camera.backgroundColor);
        renderContext.ExecuteCommandBuffer(cameraClearFlagCmd);
        CommandBufferPool.Release(cameraClearFlagCmd);

            //cameraBuffer.ClearRenderTarget(true, false, Color.clear);

        ConfigureRealtimeLights(renderContext, cullingResults);

        cameraBuffer.BeginSample("Render Camera");

        //copy the colour arrays to gpu by SetGlobalVectorArray on a cmd buffer then executing it
        /*cameraBuffer.SetGlobalVectorArray(
            visibleLightColorsId, visibleLightColors
        );

        cameraBuffer.SetGlobalVectorArray(
            visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
        );
        cameraBuffer.SetGlobalVectorArray(
            visibleLightAttenuationsId, visibleLightAttenuations
        );
        cameraBuffer.SetGlobalVectorArray(
            visibleLightSpotDirectionsId, visibleLightSpotDirections //additional array for spot light directions
        );
            renderContext.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();*/

        //use config per camera to determine what gets cleared in the render target, via its clear flags and background colour
        CameraClearFlags clearFlags = camera.clearFlags; // clear the render target with command buffers
        cameraBuffer.ClearRenderTarget((clearFlags & CameraClearFlags.Depth) != 0, (clearFlags & CameraClearFlags.Color) != 0, camera.backgroundColor);

            renderContext.ExecuteCommandBuffer(cameraBuffer);
        cameraBuffer.Clear();

        //drawing step: draw visible shapes, instruct unity to sort the renderers by distance from front to back

        var sortingSettings = new SortingSettings(camera);
        DrawingSettings drawingSettings = new DrawingSettings(srp_PassName, sortingSettings)
        {
            perObjectData = PerObjectData.LightIndices | PerObjectData.LightData
        };
         drawingSettings.enableDynamicBatching = useDynamicBatching;
         drawingSettings.enableInstancing = useGPUInstancing;

        sortingSettings.criteria = SortingCriteria.CommonOpaque;

        FilteringSettings filterSettings = new FilteringSettings(RenderQueueRange.all);
        
            //Skybox
        if (drawSkyBox)
            {
                renderContext.DrawSkybox(camera);
            }

            //Opaque objects
            sortingSettings.criteria = SortingCriteria.CommonOpaque;
            drawingSettings.sortingSettings = sortingSettings;
            filterSettings.renderQueueRange = RenderQueueRange.opaque;
            renderContext.DrawRenderers(cullingResults, ref drawingSettings, ref filterSettings);

            //Transparent objects: reverse draw order from back tofront
            sortingSettings.criteria = SortingCriteria.CommonTransparent;
            drawingSettings.sortingSettings = sortingSettings;
            //change the queue range to transparent after rendering skybox and render again
            filterSettings.renderQueueRange = RenderQueueRange.transparent;
            renderContext.DrawRenderers(cullingResults, ref drawingSettings, ref filterSettings);

        //render the default pipeline, invoke it at the end after drawing the transparent shapes to visualise error shader     
        DrawDefaultPipeline(renderContext, camera);

        renderContext.Submit();
        }
    }

    //config lights: figure out which lights are visible and have it loop through the list
    void ConfigureRealtimeLights(ScriptableRenderContext context, CullingResults cull)
    {
        for (int i = 0; i < maxVisibleLightsCount; i++)
        {
            visibleLightColors[i] = Vector4.zero;
            visibleLightData[i] = Vector4.zero;
            visibleLightSpotDirections[i] = Vector4.zero;

            if (i >= cull.visibleLights.Length)
            {
                continue;
            }

            VisibleLight light = cullingResults.visibleLights[i];            

            //keep the spot fade calculation from affecting other light types
            Vector4 attenuation = Vector4.zero;
            attenuation.w = 1f;

            if (light.lightType == LightType.Directional)
            {
                visibleLightData[i] = light.localToWorldMatrix.MultiplyVector(Vector3.back);
                visibleLightColors[i] = light.finalColor;
                visibleLightColors[i].w = -1; //for identifying it is a dir light in shader
            }
            else
            {
                if(light.lightType == LightType.Point)
                {
                    visibleLightData[i] = light.localToWorldMatrix.GetColumn(3);
                    visibleLightData[i].w = light.range;
                    visibleLightColors[i] = light.finalColor;
                    visibleLightColors[i].w = -2; //for identifying it is a point light in shader
                }

                //attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);
                //check if light is a spotlight => setup direction vector and assign to visibleLightSpotDirections
                if (light.lightType == LightType.Spot)
                {
                    visibleLightData[i].w = 1f / Mathf.Max(light.range * light.range, 0.00001f); //attenuation
                    visibleLightSpotDirections[i] = light.localToWorldMatrix.GetColumn(2);
                    visibleLightSpotDirections[i].x = -visibleLightSpotDirections[i].x;
                    visibleLightSpotDirections[i].y = -visibleLightSpotDirections[i].y;
                    visibleLightSpotDirections[i].z = -visibleLightSpotDirections[i].z;
                    visibleLightColors[i] = light.finalColor;

                    //angle falloff
                    float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
                    float outerCos = Mathf.Cos(outerRad);
                    float outerTan = Mathf.Tan(outerRad);
                    float innerCos = Mathf.Cos(Mathf.Atan((46f / 64f) * outerTan));
                    float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);

                    //spotlight attenuation
                    //attenuation.z = 1f / angleRange;
                    //attenuation.w = -outerCos * attenuation.z;
                    visibleLightSpotDirections[i].w = 1f / angleRange;
                    visibleLightColors[i].w = -outerCos * visibleLightSpotDirections[i].w;
                }
                else
                {
                    //if not a point / dir / spot light: ignore it.
                    continue;
                }
            }
        }

        for (int i = 0; i < maxVisibleLightsCount; i++)
        {
            visibleLightColors[i] = Color.clear;  //continue to loop after finishing the visible lights - clearing the colour of unused lights
        }

        CommandBuffer cmdLight = CommandBufferPool.Get("Set-up Light Buffer");
        cmdLight.SetGlobalVectorArray(visibleLightDataID, visibleLightData);
        cmdLight.SetGlobalVectorArray(visibleLightColorsId, visibleLightColors);
        cmdLight.SetGlobalVectorArray(visibleLightSpotDirectionsId, visibleLightSpotDirections);
        //cmdLight.SetGlobalVectorArray(visibleLightAttenuationsId, visibleLightAttenuations);
        context.ExecuteCommandBuffer(cmdLight);
        CommandBufferPool.Release(cmdLight);
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

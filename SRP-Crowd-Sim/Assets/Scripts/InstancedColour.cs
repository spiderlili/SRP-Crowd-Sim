using UnityEngine;

public class InstancedColour : MonoBehaviour
{

    [SerializeField]
    Color color = Color.white;
    static MaterialPropertyBlock propertyBlock;
    static int colorID = Shader.PropertyToID("_Color");

    void Awake()
    {
        OnValidate();
    }

    //invoked in edit mode when component is loaded/changed - each time scene loaded and component edited, change immediately
    void OnValidate()
    {
        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
        }
        propertyBlock.SetColor(colorID, color);
        GetComponent<MeshRenderer>().SetPropertyBlock(propertyBlock);
    }
}
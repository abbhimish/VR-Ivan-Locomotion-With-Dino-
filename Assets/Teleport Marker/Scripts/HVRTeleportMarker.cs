using UnityEngine;

public class HVRTeleportMarker : MonoBehaviour
{
    public GameObject Arrow;
    public GameObject Ring;

    public bool UseTeleporterColors = true;
    public Color ValidColor;
    public Color InvalidColor;

    protected Material RingMaterial;
    protected Material ArrowMaterial;


    public Color Color
    {
        get
        {
            if (UseTeleporterColors)
            {
                return ValidColor;
            }

            return InvalidColor;
        }
    }

    public void Awake()
    {
        if (Ring && Ring.TryGetComponent(out MeshRenderer ringRenderer)) RingMaterial = ringRenderer.material;
        if (Arrow && Arrow.TryGetComponent(out MeshRenderer arrowRenderer)) ArrowMaterial = arrowRenderer.material;
    }


    void OnActivated()
    {
        if (Arrow) Arrow.SetActive(true);
        if (Ring) Ring.SetActive(true);
    }

    void OnDeactivated()
    {
        if (Arrow) Arrow.SetActive(false);
        if (Ring) Ring.SetActive(false);
    }

    public void OnValidTeleportChanged(bool isTeleportValid)
    {
        UpdateMaterials();
    }

    void UpdateMaterials()
    {
        if (RingMaterial) RingMaterial.color = Color;
        if (ArrowMaterial) ArrowMaterial.color = Color;
    }
}
using UnityEngine;
using Unity.Netcode;

public class CharacterBombVisual : NetworkBehaviour
{
    public GameObject BombModel; // Drag your bomb model here in Unity Editor

    void Start()
    {
        BombModel.SetActive(false); // Hide bomb at start
    }

    public void ShowBomb(bool show)
    {
        BombModel.SetActive(show); // Magic show/hide bomb
    }
}

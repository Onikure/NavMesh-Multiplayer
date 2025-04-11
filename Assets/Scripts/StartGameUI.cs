using UnityEngine;
using UnityEngine.UI;
using Unity.Netcode;

public class StartGameUI : NetworkBehaviour
{
    [SerializeField] private GameObject startCanvas;
    [SerializeField] private Button startButton;
    [SerializeField] private BombManager bombManager;

    public override void OnNetworkSpawn()
    {
        if (IsHost)
        {
            startCanvas.SetActive(true);
            startButton.onClick.AddListener(OnStartClicked);
        }
        else
        {
            startCanvas.SetActive(false);
        }
    }

    private void OnStartClicked()
    {
        startCanvas.SetActive(false);
        bombManager.StartGame();
    }
}

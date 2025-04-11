using UnityEngine;
using Unity.Netcode;
using System.Linq;
using System.Collections;

public class BombManager : NetworkBehaviour
{
    [SerializeField] float minTime = 10f;
    [SerializeField] float maxTime = 20f;

    private ulong currentBombHolder;

    public void StartGame()
    {
        if (IsServer) StartCoroutine(BombTimer());
    }

    IEnumerator BombTimer()
    {
        while (true)
        {
            PassBombToNewPlayer();
            yield return new WaitForSeconds(Random.Range(minTime, maxTime));
        }
    }

    void PassBombToNewPlayer()
    {
        if (!IsServer) return;

        // 1. Remove bomb from current player
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(currentBombHolder, out var oldClient))
        {
            oldClient.PlayerObject.GetComponent<CharacterBombVisual>().ShowBomb(false);
        }

        // 2. Choose new random player
        ulong[] allPlayers = NetworkManager.Singleton.ConnectedClientsIds.ToArray();
        ulong newPlayer = allPlayers[Random.Range(0, allPlayers.Length)];

        // 3. Give bomb to new player
        NetworkManager.Singleton.ConnectedClients[newPlayer].PlayerObject.GetComponent<CharacterBombVisual>().ShowBomb(true);
        currentBombHolder = newPlayer;
    }
}

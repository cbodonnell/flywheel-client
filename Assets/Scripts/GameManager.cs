
using UnityEngine;

public class GameManager : MonoBehaviour
{
    private void OnGUI()
    {
        GUILayout.BeginArea(new Rect(10, 10, 300, 300));
        if (!NetworkManager.Instance.IsConnected)
        {
            if (GUILayout.Button("Connect"))
                NetworkManager.Instance.Connect();
        }
        else
        {
            if (!NetworkManager.Instance.IsLoggedIn)
            {
                GUILayout.Label("Waiting to be assigned an ID...");
            }
            else
            {
                GUILayout.Label($"Connected as client: {NetworkManager.Instance.ClientID}");
            }
        }
        GUILayout.EndArea();
    }
}

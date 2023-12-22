using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
	/*
	GameManager:
	- Manages player GameObjects, including creation, destruction, and position updates
	- Subscribes to network updates such as OnGameUpdateReceived event and handles player GameObjects accordingly
	*/

	// Reference to the player prefab
	public GameObject playerPrefab;

	// Dictionary to keep track of player GameObjects by their IDs
	private readonly Dictionary<uint, GameObject> playerGameObjects = new Dictionary<uint, GameObject>();

	// Duration over which the game interpolates between two states of a player:
	// Smaller values will make movement snappier
	// Larger value make it smoother, but potentially more laggy
	[SerializeField]
	public float smoothTime = 0.8f;

	// Set to 'true' to use Lerp, 'false' for SmoothDamp
	[SerializeField]
	public bool useLerpForInterpolation = true; 

	// Define a hashset to keep track of active players for each GameUpdate
	// Similar to a 'set' in Python, data structure that stores unique values
	private HashSet<uint> activePlayerIDs = new HashSet<uint>();

	// Class to store historical state of players, used for interpolation
	public class HistoricalState
	{
		public float timestamp;
		public Vector2 position;
		public Vector2 currentVelocity;
		// Add other state data as needed, like rotation
	}

	// Keeps track of historical data, such as where a player has been:
	// Used to determine where to interpolate (i.e. target position)
	private Dictionary<uint, List<HistoricalState>> playerStateHistory = new Dictionary<uint, List<HistoricalState>>();

	// Keeps track of how fast a player is currently moving:
	// Used to determine how to interpolation (i.e. rate and smoothness of movement towards target position)
    private Dictionary<uint, Vector2> playerCurrentVelocities = new Dictionary<uint, Vector2>();

	// Unity lifecycle method that is called when script instance is being loaded, executed before the game starts and before the Start() method:
	// Commonly used for initialization tasks that need to be performed once and are independent of other objects (ex: setting initial values or conditions)
	private void Awake()
	{
		// Ensure this script persists across scene loads (if needed)
		DontDestroyOnLoad(gameObject);
	}

	// Unity lifecycle method that is called once before the first frame update but after Awake() method:
	// Commonly used for intialization that depend on other objects having been initialized
	private void Start()
	{
		// Subscribes the HandleGameUpdate method to NetworkManager's OnGameUpdateReceived event
		NetworkManager.Instance.OnGameUpdateReceived += HandleGameUpdate;
	}

	// Unity lifecycle method that is called once per frame
	private void Update()
	{
		foreach (var kvp in playerGameObjects)
		{
			uint playerId = kvp.Key;
			GameObject playerObject = kvp.Value;
			// Skip local player, since position will be controlled by PlayerController.cs
			if (playerId == NetworkManager.Instance.ClientID) continue; // Skip local player

			// Check for at least two historical states, which is required for interpolating
			if (playerStateHistory.TryGetValue(playerId, out var states) && states.Count >= 2)
			{
				// Variables for 2nd to last state and last state
				var state1 = states[states.Count - 2];
				var state2 = states[states.Count - 1];

				// Interpolation factor 't': indicates how far along this interpolation path to current positon should be
				float t = (Time.time - state1.timestamp) / (state2.timestamp - state1.timestamp);

				// Clamping keeps value between 0 and 1 in this case to handle edge cases like delayed game updates
				// i.e. if value is below 0 this will return 0, if value is above 1 this will return 1
				t = Mathf.Clamp(t, 0, 1);

				Vector2 newPosition;
				if (useLerpForInterpolation)
				{
					newPosition = Vector2.Lerp(playerObject.transform.position, state2.position, t);
					Debug.Log($"Lerping: t={t}, Start={playerObject.transform.position}, End={state2.position}, NewPos={newPosition}");
				}
				else
				{
					Vector2 currentVelocity = playerCurrentVelocities[playerId];
					newPosition = Vector2.SmoothDamp(playerObject.transform.position, 
													state2.position, 
													ref currentVelocity, 
													smoothTime);
					playerCurrentVelocities[playerId] = currentVelocity;
				}

				playerObject.transform.position = new Vector3(newPosition.x, newPosition.y, playerObject.transform.position.z);
			}
		}
	}

	// Unity lifecycle method called when GameObject attached to this script is destroyed
	private void OnDestroy()
	{
		// Unsubscribes the HandleGameUpdate method to NetworkManager's OnGameUpdateReceived event
		if (NetworkManager.Instance != null)
		{
			NetworkManager.Instance.OnGameUpdateReceived -= HandleGameUpdate;
		}
	}

	// When subscribed, method is called whenever a game update is received
	public void HandleGameUpdate(ServerGameUpdatePayload payload)
	{
		HashSet<uint> updatedPlayerIDs = new HashSet<uint>();

		foreach (var playerInfo in payload.players)
		{
			uint playerId = playerInfo.Key;
			updatedPlayerIDs.Add(playerId);

			// Store historical state
			StorePlayerState(playerId, new Vector2(playerInfo.Value.p.x, playerInfo.Value.p.y), payload.timestamp);

			// Create or update players
			CreateOrUpdatePlayer(playerId);
		}

		// Check for disconnected players
		var disconnectedPlayers = new HashSet<uint>(activePlayerIDs);
		disconnectedPlayers.ExceptWith(updatedPlayerIDs);
		foreach (var playerId in disconnectedPlayers)
		{
			HandlePlayerDisconnection(playerId);
		}

		// Update the active player list
		activePlayerIDs = updatedPlayerIDs;
	}

	private void StorePlayerState(uint playerId, Vector2 position, float timestamp)
	{
		if (!playerStateHistory.ContainsKey(playerId))
		{
			playerStateHistory[playerId] = new List<HistoricalState>();
		}

		playerStateHistory[playerId].Add(new HistoricalState { position = position, timestamp = timestamp });

		// Trim the list to a reasonable size
		while (playerStateHistory[playerId].Count > 20) // for example, keep the last 20 states
		{
			playerStateHistory[playerId].RemoveAt(0);
		}

		// Update player velocities 
		if (playerStateHistory[playerId].Count >= 2)
		{
			var lastState = playerStateHistory[playerId][playerStateHistory[playerId].Count - 2];
			Vector2 positionDelta = position - lastState.position;
			float timeDelta = timestamp - lastState.timestamp;

			if (timeDelta > 0)
			{
				Vector2 newVelocity = positionDelta / timeDelta;
				playerCurrentVelocities[playerId] = newVelocity;
			}
		}
	}

	void HandlePlayerDisconnection(uint playerId)
	{
		if (playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
		{
			Destroy(playerObject);
			playerGameObjects.Remove(playerId);
		}
	}

	private void CreateOrUpdatePlayer(uint playerId)
	{
		if (!playerGameObjects.TryGetValue(playerId, out GameObject playerObject))
		{
			// Instantiate new GameObject for new players
			GameObject newPlayer = Instantiate(playerPrefab, Vector2.zero, Quaternion.identity);
			newPlayer.name = "Player_" + playerId;
			playerGameObjects.Add(playerId, newPlayer);

			// Initialize currentVelocity for new player
        	playerCurrentVelocities[playerId] = Vector2.zero;

			// Add PlayerController only if it's the local player
			if (playerId == NetworkManager.Instance.ClientID)
			{
				var controller = newPlayer.AddComponent<PlayerController>();
				controller.isLocalPlayer = true;
			}
		}
	}

	private void OnGUI()
	{
		GUILayout.BeginArea(new Rect(10, 10, 300, 300));
		if (!NetworkManager.Instance.IsConnected)
		{
			if (GUILayout.Button("Connect"))
				NetworkManager.Instance.OnConnect();
		}
		else
		{
			if (!NetworkManager.Instance.HasClientID)
			{
				GUILayout.Label("Waiting to be assigned an ID...");
			}
			else
			{
				GUILayout.Label($"Connected as client: {NetworkManager.Instance.ClientID}");

				// Create or update the local player object
				CreateOrUpdatePlayer(NetworkManager.Instance.ClientID); // Updated this line
			}

			if (GUILayout.Button("Disconnect"))
				NetworkManager.Instance.OnDisconnect();
		}
		GUILayout.EndArea();
	}
}

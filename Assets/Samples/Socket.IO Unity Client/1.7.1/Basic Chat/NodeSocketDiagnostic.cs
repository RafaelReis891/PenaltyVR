
using UnityEngine;

public class NodeSocketDiagnostic : MonoBehaviour
{
	private const string SERVER_URL = "ws://127.0.0.1:3001";

	private void Start()
	{
		if (SocketIOManager.Instance == null)
		{
			Debug.LogError("[NodeSocket] SocketIOManager não encontrado na cena!");
			return;
		}

		var socket = SocketIOManager.Instance.Socket;

		socket.OnConnected += OnConnected;
		socket.OnDisconnected += OnDisconnected;

		socket.OnError += (err) =>
		{
			Debug.LogError("[NodeSocket] Erro: " + err);
		};

		socket.On("value", (string msg) =>
		{
			Debug.Log("[NodeSocket] Atualização recebida: " + msg);
		});

		Debug.Log("[NodeSocket] Conectando em " + SERVER_URL);

		socket.Connect(SERVER_URL);
	}

	private void OnConnected()
	{
		Debug.Log("[NodeSocket] Conectado ao servidor!");

		var socket = SocketIOManager.Instance.Socket;

		socket.Emit("subscribe", new
		{
			path = "Rooms/ARENA01"
		});

		Debug.Log("[NodeSocket] Inscrição na sala ARENA01 enviada.");
	}

	private void OnDisconnected()
	{
		Debug.LogWarning("[NodeSocket] Desconectado do servidor.");
	}
}

using UnityEngine;

public class InteractableDoor : MonoBehaviour, IInteractable
{
    [Header("Door Settings")]
	[SerializeField] private string interactPrompt = " to Enter Door";
	[SerializeField] private Transform destinationPoint;
	private Transform playerTransform;

	public string GetInteractText() => interactPrompt;
	public Transform GetTransform() => transform;

	public void Interact()
	{
		TeleportPlayer();
	}

	private void TeleportPlayer()
	{
		if (destinationPoint == null) return;

		if (playerTransform == null)
		{
			GameObject player = GameObject.FindGameObjectWithTag("Player");
			if (player != null) playerTransform = player.transform;
		}

		if (playerTransform != null)
		{
			CharacterController cc = playerTransform.GetComponent<CharacterController>();
			if (cc != null) cc.enabled = false;

			playerTransform.position = destinationPoint.position;
			playerTransform.rotation = destinationPoint.rotation;

			if (cc != null) cc.enabled = true;
		}
	}
}

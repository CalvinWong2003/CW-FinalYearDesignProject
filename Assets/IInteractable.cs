using UnityEngine;

public interface IInteractable
{
    string GetInteractText(); // For the UI hint (e.g., "Talk to Bob")
	void Interact();          // What happens when E is pressed
	Transform GetTransform(); // To calculate distance
}

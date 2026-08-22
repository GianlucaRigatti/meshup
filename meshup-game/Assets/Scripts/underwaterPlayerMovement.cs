using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class UnderwaterPlayerMovement : MonoBehaviour
{
    [Header("Movimento")]
    public float swimSpeed = 5f;
    public float verticalSpeed = 4f;
    public float waterDrag = 2f; // Simula la resistenza dell'acqua

    [Header("Visuale (Mouse)")]
    public Transform cameraTransform;
    public float mouseSensitivity = 2f;
    public float upDownRange = 80f;

    private CharacterController controller;
    private float verticalRotation = 0f;
    private Vector3 moveDirection = Vector3.zero;

    void Start()
    {
        controller = GetComponent<CharacterController>();

        // Blocca il cursore del mouse al centro dello schermo
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    void Update()
    {
        GestisciVisuale();
        GestisciMovimento();
    }

    void GestisciVisuale()
    {
        // Rotazione Orizzontale (Gira il personaggio a destra/sinistra)
        float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
        transform.Rotate(Vector3.up, mouseX);

        // Rotazione Verticale (Guarda su/giù con la telecamera)
        float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
        verticalRotation -= mouseY;
        verticalRotation = Mathf.Clamp(verticalRotation, -upDownRange, upDownRange);
        cameraTransform.localRotation = Quaternion.Euler(verticalRotation, 0f, 0f);
    }

    void GestisciMovimento()
    {
        // 1. Raccogli input direzionali (WASD / Frecce)
        float moveX = Input.GetAxis("Horizontal");
        float moveZ = Input.GetAxis("Vertical");

        // 2. Calcola la direzione in base a dove guarda il giocatore
        // Usiamo cameraTransform.forward così se guardi verso l'alto e premi W, nuoti verso l'alto
        Vector3 forwardMovement = cameraTransform.forward * moveZ;
        Vector3 rightMovement = cameraTransform.right * moveX;

        Vector3 targetDirection = (forwardMovement + rightMovement).normalized * swimSpeed;

        // 3. Input per Salita/Discesa manuale (Spazio / Ctrl)
        float verticalInput = 0f;
        if (Input.GetKey(KeyCode.Space))
        {
            verticalInput = verticalSpeed; // Nuota verso l'alto
        }
        else if (Input.GetKey(KeyCode.LeftControl))
        {
            verticalInput = -verticalSpeed; // Nuota verso il basso
        }

        // Applichiamo la velocità verticale alla direzione totale
        targetDirection.y += verticalInput;

        // 4. Effetto Inerzia/Attrito (Smorzamento del movimento)
        // L'acqua non ti fa fermare all'istante, crea un effetto più morbido (Lerp)
        moveDirection = Vector3.Lerp(moveDirection, targetDirection, Time.deltaTime * waterDrag);

        // 5. Muovi effettivamente il Character Controller
        controller.Move(moveDirection * Time.deltaTime);
    }
}
using UnityEngine;

namespace Meshup.Lobby
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class LobbyFirstPersonController : MonoBehaviour
    {
        [SerializeField] private Transform viewCamera;
        [SerializeField] private float moveSpeed = 5f;
        [SerializeField] private float sprintMultiplier = 1.6f;
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float verticalLookLimit = 80f;
        [SerializeField] private float gravity = -20f;

        private CharacterController characterController;
        private float pitch;
        private float verticalVelocity;
        private bool inputEnabled = true;

        public bool InputEnabled => inputEnabled;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (viewCamera == null && Camera.main != null)
            {
                viewCamera = Camera.main.transform;
            }
        }

        private void Start()
        {
            SetInputEnabled(true);
        }

        private void Update()
        {
            if (!inputEnabled)
            {
                return;
            }

            var mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            var mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;
            transform.Rotate(Vector3.up, mouseX);
            pitch = Mathf.Clamp(pitch - mouseY,
                -verticalLookLimit, verticalLookLimit);
            if (viewCamera != null)
            {
                viewCamera.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }

            var move = transform.right * Input.GetAxisRaw("Horizontal")
                + transform.forward * Input.GetAxisRaw("Vertical");
            move = Vector3.ClampMagnitude(move, 1f);
            var speed = Input.GetKey(KeyCode.LeftShift)
                ? moveSpeed * sprintMultiplier
                : moveSpeed;

            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }
            verticalVelocity += gravity * Time.deltaTime;
            move = move * speed + Vector3.up * verticalVelocity;
            characterController.Move(move * Time.deltaTime);
        }

        public void SetInputEnabled(bool enabled)
        {
            inputEnabled = enabled;
            if (!enabled)
            {
                verticalVelocity = 0f;
            }
            Cursor.lockState = enabled
                ? CursorLockMode.Locked
                : CursorLockMode.None;
            Cursor.visible = !enabled;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}

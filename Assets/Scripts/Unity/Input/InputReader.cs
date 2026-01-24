using UnityEngine;
using UnityEngine.InputSystem;

namespace Peribind.Unity.Input
{
    public class InputReader : MonoBehaviour
    {
        [Header("Pointer")]
        [SerializeField] private InputActionReference pointAction;
        [SerializeField] private InputActionReference clickAction;

        [Header("Rotation")]
        [SerializeField] private InputActionReference rotateLeftAction;
        [SerializeField] private InputActionReference rotateRightAction;

        [Header("Selection")]
        [SerializeField] private InputActionReference selectPreviousAction;
        [SerializeField] private InputActionReference selectNextAction;

        [Header("Cancel")]
        [SerializeField] private InputActionReference cancelAction;

        private bool _placePressed;
        private int _rotateStep;
        private int _selectStep;
        private bool _cancelPressed;

        public Vector2 PointerPosition => pointAction != null ? pointAction.action.ReadValue<Vector2>() : Vector2.zero;

        private void OnEnable()
        {
            EnableAction(pointAction);
            EnableAction(clickAction);
            EnableAction(rotateLeftAction);
            EnableAction(rotateRightAction);
            EnableAction(selectPreviousAction);
            EnableAction(selectNextAction);
            EnableAction(cancelAction);

            if (clickAction != null)
            {
                clickAction.action.performed += OnClickPerformed;
            }

            if (rotateLeftAction != null)
            {
                rotateLeftAction.action.performed += OnRotateLeftPerformed;
            }

            if (rotateRightAction != null)
            {
                rotateRightAction.action.performed += OnRotateRightPerformed;
            }

            if (selectPreviousAction != null)
            {
                selectPreviousAction.action.performed += OnSelectPreviousPerformed;
            }

            if (selectNextAction != null)
            {
                selectNextAction.action.performed += OnSelectNextPerformed;
            }

            if (cancelAction != null)
            {
                cancelAction.action.performed += OnCancelPerformed;
            }

        }

        private void OnDisable()
        {
            if (clickAction != null)
            {
                clickAction.action.performed -= OnClickPerformed;
            }

            if (rotateLeftAction != null)
            {
                rotateLeftAction.action.performed -= OnRotateLeftPerformed;
            }

            if (rotateRightAction != null)
            {
                rotateRightAction.action.performed -= OnRotateRightPerformed;
            }

            if (selectPreviousAction != null)
            {
                selectPreviousAction.action.performed -= OnSelectPreviousPerformed;
            }

            if (selectNextAction != null)
            {
                selectNextAction.action.performed -= OnSelectNextPerformed;
            }

            if (cancelAction != null)
            {
                cancelAction.action.performed -= OnCancelPerformed;
            }

            DisableAction(pointAction);
            DisableAction(clickAction);
            DisableAction(rotateLeftAction);
            DisableAction(rotateRightAction);
            DisableAction(selectPreviousAction);
            DisableAction(selectNextAction);
            DisableAction(cancelAction);
        }

        public bool ConsumePlacePressed()
        {
            if (!_placePressed)
            {
                return false;
            }

            _placePressed = false;
            return true;
        }

        public void ClearPlacePressed()
        {
            _placePressed = false;
        }

        public int ConsumeRotateStep()
        {
            var step = _rotateStep;
            _rotateStep = 0;
            return step;
        }

        public int ConsumeSelectStep()
        {
            var step = _selectStep;
            _selectStep = 0;
            return step;
        }

        public bool ConsumeCancelPressed()
        {
            if (!_cancelPressed)
            {
                return false;
            }

            _cancelPressed = false;
            return true;
        }

        private void OnClickPerformed(InputAction.CallbackContext context)
        {
            _placePressed = true;
        }

        private void OnRotateLeftPerformed(InputAction.CallbackContext context)
        {
            _rotateStep -= 1;
        }

        private void OnRotateRightPerformed(InputAction.CallbackContext context)
        {
            _rotateStep += 1;
        }

        private void OnSelectPreviousPerformed(InputAction.CallbackContext context)
        {
            _selectStep -= 1;
        }

        private void OnSelectNextPerformed(InputAction.CallbackContext context)
        {
            _selectStep += 1;
        }

        private void OnCancelPerformed(InputAction.CallbackContext context)
        {
            _cancelPressed = true;
        }

        private static void EnableAction(InputActionReference actionReference)
        {
            if (actionReference != null)
            {
                actionReference.action.Enable();
            }
        }

        private static void DisableAction(InputActionReference actionReference)
        {
            if (actionReference != null)
            {
                actionReference.action.Disable();
            }
        }
    }
}

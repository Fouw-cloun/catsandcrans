using UnityEngine;

namespace CatCooks.Food
{
    /// <summary>
    /// Помощник для инструментов в руке (грохот, шприц): кто держит, его камера и «замороженный взгляд».
    ///
    /// Как у ножовки: пока трясёшь грохот, мышь уходит в тряску, а камера стоит. Камера стоит через FirstPersonController.HoldLook — углы не копятся, отпустил — камера там же.
    ///
    /// Звать: Pose(handAnchor) — из UpdateHeldPose; Late(freeze, stopMoving) — из LateUpdate у владельца;
    /// Release() — при «выронил».
    /// </summary>
    public sealed class HeldToolLook
    {
        private FirstPersonController _fpc;
        private Transform _cam;
        private int _poseFrame = -10;
        private bool _frozen, _speedTaken;
        private Quaternion _look;

        /// <summary>Предмет сейчас в руке (рука вела его в последние кадры).</summary>
        public bool Held => Time.frameCount - _poseFrame <= 2;
        public Transform Camera => _cam;

        public void Pose(Transform handAnchor)
        {
            _poseFrame = Time.frameCount;
            if (_fpc == null && handAnchor != null) _fpc = handAnchor.GetComponentInParent<FirstPersonController>();
            if (_cam == null && _fpc != null)
            {
                Camera c = _fpc.GetComponentInChildren<Camera>(true);
                if (c != null) _cam = c.transform;
            }
        }

        /// <summary>Каждый кадр у владельца, ПОСЛЕ контроллера.</summary>
        public void Late(bool freeze, bool stopMoving)
        {
            bool on = freeze && Held && _cam != null;
            if (on)
            {
                if (!_frozen) { _frozen = true; _look = _cam.rotation; }
                if (_fpc != null) _fpc.HoldLook();      // обзор на паузе, углы целы — после отпускания без рывка
                else _cam.rotation = _look;
            }
            else if (_frozen)
            {
                _frozen = false;                         // HoldLook больше не зовём — обзор вернётся сам
            }
            bool stop = stopMoving && Held && _fpc != null;
            if (stop && !_speedTaken) { _fpc.SpeedMultiplier = 0f; _speedTaken = true; }
            else if (!stop && _speedTaken) { if (_fpc != null) _fpc.SpeedMultiplier = 1f; _speedTaken = false; }
        }

        public void Release()
        {
            if (_fpc != null)
            {
                if (_speedTaken) _fpc.SpeedMultiplier = 1f;
            }
            _speedTaken = false; _frozen = false;
            _fpc = null; _cam = null;
        }
    }
}

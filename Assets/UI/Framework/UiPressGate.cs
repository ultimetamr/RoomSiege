namespace RoomSiege.UI
{
    // A page transition consumes the physical press, independently of XRI model resets.
    public sealed class UiPressGate
    {
        private bool blocked;
        public bool IsHeld { get; private set; }
        public bool Output { get; private set; }

        public bool Update(bool held)
        {
            IsHeld = held;
            if (!held) blocked = false;
            Output = held && !blocked;
            return Output;
        }

        public void BlockUntilRelease()
        {
            blocked = true;
            Output = false;
        }
    }
}

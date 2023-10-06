namespace SpotEngine
{
    /// <summary>
    /// An action can be activated
    /// by different keys or buttons.
    /// </summary>
    public class InputAction
    {
        public string[] keys;

        public InputAction(string[] action)
        {
            this.keys = action;
        }

        public InputAction(string action)
        {
            this.keys = new string[] {action};
        }
    }
}

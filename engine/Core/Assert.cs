namespace SpotEngine
{
    /// <summary>
    /// Provides static methods to perform assertions for unit tests and validation.
    /// </summary>
    public static class Assert
    {
        /// <summary>
        /// Asserts that a condition is true.
        /// </summary>
        /// <param name="condition">The condition to evaluate.</param>
        /// <param name="message">The message to display if the assertion fails.</param>
        /// <exception cref="AssertionException">Thrown when the condition is false.</exception>
        public static void IsTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new AssertionException(message);
            }
        }

        /// <summary>
        /// Asserts that a condition is false.
        /// </summary>
        /// <param name="condition">The condition to evaluate.</param>
        /// <param name="message">The message to display if the assertion fails.</param>
        /// <exception cref="AssertionException">Thrown when the condition is true.</exception>
        public static void IsFalse(bool condition, string message)
        {
            if (condition)
            {
                throw new AssertionException(message);
            }
        }

        /// <summary>
        /// Asserts that an object is null.
        /// </summary>
        /// <param name="obj">The object to check.</param>
        /// <param name="message">The message to display if the assertion fails.</param>
        /// <exception cref="AssertionException">Thrown when the object is not null.</exception>
        public static void IsNull(object obj, string message)
        {
            if (obj != null)
            {
                throw new AssertionException(message);
            }
        }

        /// <summary>
        /// Asserts that an object is not null.
        /// </summary>
        /// <param name="obj">The object to check.</param>
        /// <param name="message">The message to display if the assertion fails.</param>
        /// <exception cref="AssertionException">Thrown when the object is null.</exception>
        public static void IsNotNull(object obj, string message)
        {
            if (obj == null)
            {
                throw new AssertionException(message);
            }
        }

        /// <summary>
        /// Asserts that two values are equal.
        /// </summary>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value to compare.</param>
        /// <param name="message">The message to display if the assertion fails.</param>
        /// <exception cref="AssertionException">Thrown when the values are not equal.</exception>
        public static void AreEqual(object expected, object actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new AssertionException(message);
            }
        }

        /// <summary>
        /// Asserts that two values are not equal.
        /// </summary>
        /// <param name="expected">The expected value.</param>
        /// <param name="actual">The actual value to compare.</param>
        /// <param name="message">The message to display if the assertion fails.</param>
        /// <exception cref="AssertionException">Thrown when the values are equal.</exception>
        public static void AreNotEqual(object expected, object actual, string message)
        {
            if (object.Equals(expected, actual))
            {
                throw new AssertionException(message);
            }
        }
    }

    /// <summary>
    /// Represents errors that occur during assertion checks.
    /// </summary>
    public class AssertionException : Exception
    {
        public AssertionException(string message) : base(message) { }
    }
}

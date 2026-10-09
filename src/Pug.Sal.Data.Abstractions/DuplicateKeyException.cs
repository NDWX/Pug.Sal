namespace Pug.Sal.Data.Abstractions
{
	// Thrown by an IDataSession insert when a record with the same key already exists, retired or
	// not. Store-neutral, so the facade can report a DuplicateEntryException without knowing the
	// store; other constraint violations (a missing parent row) surface as the store's own exception.
	public class DuplicateKeyException : Exception
	{
		public DuplicateKeyException( string message, Exception? innerException = null )
			: base( message, innerException )
		{
		}
	}
}

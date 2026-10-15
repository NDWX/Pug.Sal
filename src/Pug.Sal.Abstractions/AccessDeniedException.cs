using System;

namespace Pug.Sal.Abstractions
{
	// Thrown when the caller may not perform an operation: there is no authenticated principal, or
	// it does not hold the SecurityOperations value the operation requires (DECISIONS D8, D25).
	// Over HTTP it is a 403. It derives from UnauthorizedAccessException so that code catching that
	// still sees a refusal, but Sal throws and maps only this type: an operating-system access
	// failure (an UnauthorizedAccessException of its own) is a server fault, never reported as a
	// refusal.
	public class AccessDeniedException : UnauthorizedAccessException
	{
		// The SecurityOperations value refused; null when unknown, as for a 403 the HTTP client
		// receives (the host's scope or policy gate, or a facade refusal, carry no body).
		public string Operation { get; }

		public AccessDeniedException( string operation = null )
			: base( operation == null ? "Access denied." : $"Access denied: the caller may not perform {operation}." )
		{
			Operation = operation;
		}
	}
}

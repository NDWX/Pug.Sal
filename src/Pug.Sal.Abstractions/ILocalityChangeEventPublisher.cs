using System.Threading.Tasks;
using Pug.Sal.Models;

namespace Pug.Sal.Abstractions
{
	// The host's event broker, for audit logging when the host wants it; Sal uses a no-op
	// publisher when none is registered. Called after the write committed, best-effort: an
	// exception is logged and swallowed, never failing or undoing the write.
	public interface ILocalityChangeEventPublisher
	{
		// One register, update, retire, reinstate or delete made through ILocalityAdministration.
		Task PublishAsync( LocalityChange change );

		// One completed import, with its counts; published instead of any per-row change.
		Task PublishImportAsync( ImportRun run );
	}
}

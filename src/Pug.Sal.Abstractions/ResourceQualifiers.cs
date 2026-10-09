using Pug.Application.Security;

namespace Pug.Sal.Abstractions
{
	// The resources Sal authorizes against. Every check is made against AllCountries: there are no
	// country-specific grants for now. Domain is empty: reference data is global, not
	// tenant-scoped.
	public static class ResourceQualifiers
	{
		public static NounQualifier AllCountries => new NounQualifier
		{
			Type = SecurityObjectTypes.Country
		};
	}
}

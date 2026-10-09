using System.ComponentModel.DataAnnotations;
using System.Runtime.Serialization;

namespace Pug.Sal.WebApi.Models
{
	// Body of a 400 response: which request argument was rejected and why. Pug.Sal.Client rebuilds
	// the ArgumentException the facade threw from it.
	[DataContract]
	public record BadRequestResultInfo
	{
		public BadRequestResultInfo()
		{
			Message = string.Empty;
			Type = string.Empty;
			RequestArgument = string.Empty;
		}

		public BadRequestResultInfo( string type, string requestArgument, string message = "" )
		{
			Type = type;
			RequestArgument = requestArgument;
			Message = message;
		}

		[Required( AllowEmptyStrings = true ), DataMember]
		public string Message
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		// MISSING_ARGUMENT or INVALID_ARGUMENT.
		[Required( AllowEmptyStrings = true ), DataMember]
		public string Type
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}

		[Required( AllowEmptyStrings = true ), DataMember]
		public string RequestArgument
		{
			get;
#if NET5_0_OR_GREATER
			init;
#else
			set;
#endif
		}
	}
}

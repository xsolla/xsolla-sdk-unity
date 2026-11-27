namespace Xsolla.Core
{
	internal enum ErrorType
	{
		Undefined,

		UnknownError,
		NetworkError,

		InvalidToken,
		AuthorizationHeaderNotSent,

		MethodIsNotAllowed,
		NotSupportedOnCurrentPlatform,

		InvalidData,
		ProductDoesNotExist,
		PayStationServiceException,
		UserNotFound,
		CartNotFound,
		OrderNotFound,
		InvalidCoupon,

		PasswordResetNotAllowedForProject,
		RegistrationNotAllowedException,
		TokenVerificationException,
		UsernameIsTaken,
		EmailIsTaken,
		UserIsNotActivated,
		CaptchaRequiredException,
		InvalidProjectSettings,
		InvalidLoginOrPassword,
		InvalidAuthorizationCode,
		ExceededAuthorizationCodeAttempts,
		MultipleLoginUrlsException,
		SubmittedLoginUrlNotFoundException,

		IncorrectFriendState,

		TimeLimitReached,

		Unauthorized,
		
		UserCancelled,
		
		OrderInfoWrongAccessToken,
		OrderInfoNoInvoices,
		OrderInfoInvalidStatus,
		OrderInfoDoneButInvalidInvoiceId,
		OrderInfoDoneButInvalidOrderId,
		OrderInfoUnreachable,
		
		Aborted,

		OrderCanceled
	}
}
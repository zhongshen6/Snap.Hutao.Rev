#pragma once

using namespace winrt;

namespace Service::Utils
{

	namespace Message
	{
		enum Severity
		{
			Info = 0,
			Warn,
			Error
		};

		void ShowMessageBox(const wchar_t* message, Severity level);

	}
	hstring ResourceGetString(const wchar_t* resourceId);
	
}

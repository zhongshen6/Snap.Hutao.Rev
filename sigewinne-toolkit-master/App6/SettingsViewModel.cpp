#include "pch.h"
#include "SettingsViewModel.h"
#if __has_include("SettingsViewModel.g.cpp")
#include "SettingsViewModel.g.cpp"
#endif

namespace winrt::App6::implementation
{

	bool SettingsViewModel::StealthMode()
	{
		return pappsettings->stealthmode();
	}

	void SettingsViewModel::StealthMode(bool value)
	{
		pappsettings->set_stealthmode(value);
	}

	bool SettingsViewModel::RestrictedTokens()
	{
		return pappsettings->restrictedtokens();
	}

	void SettingsViewModel::RestrictedTokens(bool value)
	{
		pappsettings->set_restrictedtokens(value);
	}

	bool SettingsViewModel::LangOverride()
	{
		return pappsettings->langoverride();
	}

	void SettingsViewModel::LangOverride(bool value)
	{
		pappsettings->set_langoverride(value);
	}
	

}
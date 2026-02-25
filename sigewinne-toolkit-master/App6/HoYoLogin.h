#pragma once

#include <string>
#include <vector>
#include <iostream>
#include <winrt/Windows.Foundation.h>

using namespace winrt;
using namespace Windows::Foundation;

namespace Service::Game::Account
{
	class HoYoLogin
	{
	public:
		HoYoLogin(std::string& cookies);
		IAsyncAction postRequest();
		std::string& cookie();
		std::string ticket;
	private:
		void initParms();
		std::string m_cookies;
		uint32_t m_account_id{};
		std::string_view m_stoken;
		std::string_view m_ltoken;
		std::string_view m_ltuid;
		std::string_view m_mid;
		std::string m_ticket;
		static constexpr wchar_t m_url[] = L"https://passport-api.mihoyo.com/account/ma-cn-verifier/app/createAuthTicketByGameBiz";
	};


}

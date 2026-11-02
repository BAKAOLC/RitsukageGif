#include <windows.h>
#include <shobjidl.h>
#include <shlwapi.h>
#include <atomic>
#include <new>
#include <string>
#include <vector>

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** result);

namespace
{
    constexpr CLSID CommandId{0x52d7d39b, 0xe6b1, 0x4e79, {0xa3, 0xa5, 0x98, 0x61, 0x19, 0x97, 0xd7, 0x4a}};
    constexpr wchar_t OptionsKey[] = L"Software\\RitsukageGif\\ShellIntegration";
    std::atomic<long> objects{};
    std::atomic<long> locks{};

    DWORD ReadMode() noexcept
    {
        DWORD mode = 0;
        DWORD bytes = sizeof(mode);
        if (RegGetValueW(HKEY_CURRENT_USER, OptionsKey, L"ContextMenuMode", RRF_RT_REG_DWORD,
            nullptr, &mode, &bytes) != ERROR_SUCCESS || mode > 2) return 0;
        return mode;
    }

    HRESULT CopyString(const wchar_t* value, wchar_t** output) noexcept
    {
        if (!output) return E_POINTER;
        *output = nullptr;
        const auto size = (wcslen(value) + 1) * sizeof(wchar_t);
        auto copy = static_cast<wchar_t*>(CoTaskMemAlloc(size));
        if (!copy) return E_OUTOFMEMORY;
        memcpy(copy, value, size);
        *output = copy;
        return S_OK;
    }

    HRESULT ExecutablePath(std::wstring& result)
    {
        HMODULE module = nullptr;
        if (!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
            reinterpret_cast<LPCWSTR>(&DllGetClassObject), &module)) return HRESULT_FROM_WIN32(GetLastError());
        std::wstring path(32768, L'\0');
        const auto length = GetModuleFileNameW(module, path.data(), static_cast<DWORD>(path.size()));
        if (length == 0) return HRESULT_FROM_WIN32(GetLastError());
        if (length >= path.size()) return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
        path.resize(length);
        const auto slash = path.find_last_of(L"\\/");
        if (slash == std::wstring::npos) return E_UNEXPECTED;
        result.assign(path, 0, slash + 1);
        result.append(L"RitsukageGif.exe");
        return S_OK;
    }

    bool IsImage(const wchar_t* path) noexcept
    {
        const wchar_t* extension = PathFindExtensionW(path);
        constexpr const wchar_t* images[]{L".png", L".jpg", L".jpeg", L".gif", L".bmp", L".webp", L".apng",
            L".tif", L".tiff", L".ico", L".avif", L".heic", L".svg"};
        for (const auto image : images) if (_wcsicmp(extension, image) == 0) return true;
        return false;
    }

    HRESULT SelectedPaths(IShellItemArray* items, DWORD mode, std::vector<std::wstring>* paths)
    {
        if (!items || mode == 0) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        DWORD count = 0;
        HRESULT result = items->GetCount(&count);
        if (FAILED(result)) return result;
        if (count == 0) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        if (paths) paths->reserve(count);
        for (DWORD index = 0; index < count; ++index)
        {
            IShellItem* item = nullptr;
            result = items->GetItemAt(index, &item);
            if (FAILED(result)) return result;
            SFGAOF attributes = 0;
            result = item->GetAttributes(SFGAO_FILESYSTEM | SFGAO_FOLDER, &attributes);
            wchar_t* name = nullptr;
            if (SUCCEEDED(result) && (attributes & SFGAO_FILESYSTEM) != 0 && (attributes & SFGAO_FOLDER) == 0)
                result = item->GetDisplayName(SIGDN_FILESYSPATH, &name);
            else result = HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
            item->Release();
            if (FAILED(result)) return result;
            const bool supported = mode == 2 || IsImage(name);
            if (supported && paths) paths->emplace_back(name);
            CoTaskMemFree(name);
            if (!supported) return HRESULT_FROM_WIN32(ERROR_NOT_SUPPORTED);
        }
        return S_OK;
    }

    void AppendArgument(std::wstring& command, const std::wstring& argument)
    {
        if (!command.empty()) command.push_back(L' ');
        command.push_back(L'"');
        size_t slashes = 0;
        for (const auto character : argument)
        {
            if (character == L'\\') { ++slashes; continue; }
            command.append(character == L'\"' ? slashes * 2 + 1 : slashes, L'\\');
            command.push_back(character);
            slashes = 0;
        }
        command.append(slashes * 2, L'\\');
        command.push_back(L'\"');
    }

    HRESULT WriteHandoff(const std::vector<std::wstring>& paths, std::wstring& output)
    {
        std::wstring json = L"[";
        for (size_t index = 0; index < paths.size(); ++index)
        {
            if (index) json.push_back(L',');
            json.push_back(L'\"');
            for (const auto character : paths[index])
            {
                if (character == L'\\' || character == L'\"') json.push_back(L'\\');
                json.push_back(character);
            }
            json.push_back(L'\"');
        }
        json.push_back(L']');
        const int bytes = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, json.data(), static_cast<int>(json.size()),
            nullptr, 0, nullptr, nullptr);
        if (!bytes) return HRESULT_FROM_WIN32(GetLastError());
        std::string utf8(static_cast<size_t>(bytes), '\0');
        if (!WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, json.data(), static_cast<int>(json.size()),
            utf8.data(), bytes, nullptr, nullptr)) return HRESULT_FROM_WIN32(GetLastError());
        wchar_t temporary[MAX_PATH]{};
        const auto length = GetTempPathW(MAX_PATH, temporary);
        if (!length || length >= MAX_PATH) return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
        std::wstring directory = temporary;
        directory += L"RitsukageGif";
        if (!CreateDirectoryW(directory.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
            return HRESULT_FROM_WIN32(GetLastError());
        directory += L"\\shell";
        if (!CreateDirectoryW(directory.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS)
            return HRESULT_FROM_WIN32(GetLastError());
        GUID id{};
        auto status = CoCreateGuid(&id);
        if (FAILED(status)) return status;
        wchar_t identifier[40]{};
        StringFromGUID2(id, identifier, 40);
        output = directory + L"\\RitsukageGif-shell-" + identifier + L".temp";
        const auto* filename = output.c_str();
        const auto file = CreateFileW(filename, GENERIC_WRITE, 0, nullptr, CREATE_NEW, FILE_ATTRIBUTE_TEMPORARY, nullptr);
        if (file == INVALID_HANDLE_VALUE)
        {
            const auto error = GetLastError(); output.clear();
            return HRESULT_FROM_WIN32(error);
        }
        DWORD written = 0;
        const auto success = WriteFile(file, utf8.data(), static_cast<DWORD>(utf8.size()), &written, nullptr);
        const auto error = success ? ERROR_WRITE_FAULT : GetLastError();
        CloseHandle(file);
        if (!success || written != utf8.size())
        {
            DeleteFileW(filename); return HRESULT_FROM_WIN32(error);
        }
        return S_OK;
    }

    class ExplorerCommand final : public IExplorerCommand
    {
        std::atomic<ULONG> references_{1};
    public:
        ExplorerCommand() noexcept { ++objects; }
        ~ExplorerCommand() { --objects; }
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IUnknown && iid != IID_IExplorerCommand) return E_NOINTERFACE;
            *value = static_cast<IExplorerCommand*>(this);
            AddRef(); return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto remaining = --references_;
            if (!remaining) delete this;
            return remaining;
        }
        HRESULT STDMETHODCALLTYPE GetTitle(IShellItemArray*, wchar_t** title) override
        {
            wchar_t caption[256]{};
            DWORD bytes = sizeof(caption);
            if (RegGetValueW(HKEY_CURRENT_USER, OptionsKey, L"Caption", RRF_RT_REG_SZ,
                nullptr, caption, &bytes) == ERROR_SUCCESS && caption[0]) return CopyString(caption, title);
            return CopyString(PRIMARYLANGID(GetUserDefaultUILanguage()) == LANG_CHINESE ? L"贴图" : L"Pin to screen", title);
        }
        HRESULT STDMETHODCALLTYPE GetIcon(IShellItemArray*, wchar_t** icon) override
        {
            if (!icon) return E_POINTER;
            *icon = nullptr;
            try
            {
                std::wstring path;
                const auto result = ExecutablePath(path);
                if (FAILED(result)) return result;
                return CopyString((L"\"" + path + L"\",0").c_str(), icon);
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        }
        HRESULT STDMETHODCALLTYPE GetToolTip(IShellItemArray*, wchar_t** tooltip) override
        {
            if (!tooltip) return E_POINTER;
            *tooltip = nullptr; return E_NOTIMPL;
        }
        HRESULT STDMETHODCALLTYPE GetCanonicalName(GUID* id) override
        {
            if (!id) return E_POINTER;
            *id = CommandId; return S_OK;
        }
        HRESULT STDMETHODCALLTYPE GetState(IShellItemArray* items, BOOL, EXPCMDSTATE* state) override
        {
            if (!state) return E_POINTER;
            *state = ECS_HIDDEN;
            try
            {
                if (SUCCEEDED(SelectedPaths(items, ReadMode(), nullptr))) *state = ECS_ENABLED;
                return S_OK;
            }
            catch (const std::bad_alloc&) { return E_OUTOFMEMORY; }
        }
        HRESULT STDMETHODCALLTYPE Invoke(IShellItemArray* items, IBindCtx*) override
        {
            std::wstring handoff;
            try
            {
                std::vector<std::wstring> paths;
                auto result = SelectedPaths(items, ReadMode(), &paths);
                if (FAILED(result)) return result;
                std::wstring executable;
                result = ExecutablePath(executable);
                if (FAILED(result)) return result;
                std::wstring command;
                AppendArgument(command, executable);
                command.append(L" pin-files");
                for (const auto& path : paths) AppendArgument(command, path);
                if (command.size() >= 32767)
                {
                    result = WriteHandoff(paths, handoff);
                    if (FAILED(result)) return result;
                    command.clear(); AppendArgument(command, executable);
                    command.append(L" pin-files-list"); AppendArgument(command, handoff);
                }
                STARTUPINFOW startup{sizeof(startup)};
                PROCESS_INFORMATION process{};
                if (!CreateProcessW(executable.c_str(), command.data(), nullptr, nullptr, FALSE,
                    CREATE_UNICODE_ENVIRONMENT, nullptr, nullptr, &startup, &process))
                {
                    const auto error = GetLastError();
                    if (!handoff.empty()) DeleteFileW(handoff.c_str());
                    return HRESULT_FROM_WIN32(error);
                }
                CloseHandle(process.hThread); CloseHandle(process.hProcess);
                return S_OK;
            }
            catch (const std::bad_alloc&)
            {
                if (!handoff.empty()) DeleteFileW(handoff.c_str());
                return E_OUTOFMEMORY;
            }
        }
        HRESULT STDMETHODCALLTYPE GetFlags(EXPCMDFLAGS* flags) override
        {
            if (!flags) return E_POINTER;
            *flags = ECF_DEFAULT; return S_OK;
        }
        HRESULT STDMETHODCALLTYPE EnumSubCommands(IEnumExplorerCommand** commands) override
        {
            if (!commands) return E_POINTER;
            *commands = nullptr; return E_NOTIMPL;
        }
    };

    class CommandFactory final : public IClassFactory
    {
        std::atomic<ULONG> references_{1};
    public:
        CommandFactory() noexcept { ++objects; }
        ~CommandFactory() { --objects; }
        HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
            *value = static_cast<IClassFactory*>(this);
            AddRef(); return S_OK;
        }
        ULONG STDMETHODCALLTYPE AddRef() override { return ++references_; }
        ULONG STDMETHODCALLTYPE Release() override
        {
            const auto remaining = --references_;
            if (!remaining) delete this;
            return remaining;
        }
        HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** value) override
        {
            if (!value) return E_POINTER;
            *value = nullptr;
            if (outer) return CLASS_E_NOAGGREGATION;
            auto command = new (std::nothrow) ExplorerCommand;
            if (!command) return E_OUTOFMEMORY;
            const auto result = command->QueryInterface(iid, value);
            command->Release(); return result;
        }
        HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override
        {
            if (lock) ++locks; else --locks;
            return S_OK;
        }
    };
}

extern "C" HRESULT __stdcall DllCanUnloadNow()
{
    return objects == 0 && locks == 0 ? S_OK : S_FALSE;
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID clsid, REFIID iid, void** result)
{
    if (!result) return E_POINTER;
    *result = nullptr;
    if (clsid != CommandId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new (std::nothrow) CommandFactory;
    if (!factory) return E_OUTOFMEMORY;
    const auto status = factory->QueryInterface(iid, result);
    factory->Release(); return status;
}

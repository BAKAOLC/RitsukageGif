#define _DARWIN_C_SOURCE 1
#include <errno.h>
#include <mach-o/dyld.h>
#include <spawn.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/wait.h>
#include <sysexits.h>
#include <unistd.h>

extern char **environ;

int main(void)
{
    uint32_t size = 0;
    (void)_NSGetExecutablePath(NULL, &size);
    char *path = malloc(size);
    if (!path) return EX_OSERR;
    if (_NSGetExecutablePath(path, &size) != 0) {
        free(path);
        return EX_OSERR;
    }
    char *bundle = realpath(path, NULL);
    free(path);
    if (!bundle) return EX_OSERR;
    const char *suffix = "/Contents/MacOS/RitsukageGif.Login";
    const size_t length = strlen(bundle);
    const size_t suffix_length = strlen(suffix);
    if (length <= suffix_length || strcmp(bundle + length - suffix_length, suffix) != 0) {
        free(bundle);
        return EX_CONFIG;
    }
    bundle[length - suffix_length] = '\0';
    char *arguments[] = {"/usr/bin/open", "-g", "-n", "-a", bundle, "--args", "--serve", NULL};
    pid_t child;
    const int result = posix_spawn(&child, arguments[0], NULL, NULL, arguments, environ);
    free(bundle);
    if (result != 0) {
        fprintf(stderr, "Login launcher could not start the application: %s\n", strerror(result));
        return EX_OSERR;
    }
    int status;
    while (waitpid(child, &status, 0) == -1) {
        if (errno != EINTR) return EX_OSERR;
    }
    return WIFEXITED(status) ? WEXITSTATUS(status) : EX_OSERR;
}

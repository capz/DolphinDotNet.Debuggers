# Rider startup investigation

On 2026-10-29, a user reported that Rider failed to start after installing the
plugin and restarting. Removing the Rider user-data directory restored startup.
The underlying cause remains unresolved; this change does not claim to fix it.

The preserved startup log reports `ClassFormatError: Invalid constant pool index
227` in `org.jetbrains.jps.service.impl.JpsServiceManagerImpl`, attributed to
`com.intellij`. The exception is raised while Rider initializes its file-type
manager. It contains no DolphinDotNet stack frames. The currently installed
`jps-model.jar` passes ZIP CRC verification. These observations do not establish
whether the earlier failure came from cached IDE state, an installation problem,
or an interaction with a plugin.

With Rider `RD-262.10968.170` and its bundled JBR 25.0.4 on Windows:

- The downloaded 0.3.0 package and the branch's 0.3.1 package load in separate,
  fresh profiles without reproducing that exception.
- With 0.3.1 loaded, a solution opens and Rider reaches its welcome screen after
  exiting and starting the same profile again.
- Neither package bundles IntelliJ or JPS classes. The new package verification
  checks outer ZIP and inner JAR CRCs, and rejects such classes, including entries
  in multi-release JARs. These are preventative checks, not a demonstrated repair
  for the reported failure.

The original failing profile is unavailable for a controlled reproduction.
Successful fresh-profile checks do not prove that an upgrade in that profile
would work, or validate USB Gecko hardware debugging.

If startup fails again, keep `idea.log` and the exact plugin ZIP. With Rider
closed, move only `plugins/DolphinDotNet.Rider` outside its configuration directory
to disable the plugin; preserve the remaining settings. Record whether Rider
then starts with the same caches. This distinguishes a plugin-dependent failure
without erasing the evidence needed to investigate it.

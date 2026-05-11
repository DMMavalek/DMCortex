#!/bin/csh

# WebHelp
# This script launches Netscape Navigator with a specified URL.
# The first (and only) argument can be an explicit URL or it can
# be a logical ID that will be mapped to a URL.

# usage: WebHelp <url-or-id>
# example: WebHelp WebHelpInstall
# example: WebHelp http://www.blue-sky.com/
#
# Logical IDs are mapped in the switch statement below.

setenv BROWSER netscape

# This script requires an argument
if ("$1" == "") exit

# Modify these defaults if you wish. To use the normal browser
# default geometry on the user's machine, remove everything after "splash"
# (but be sure to end the line with a "
setenv NS_START_ARGS "-no-about-splash -dont-save-geometry-prefs -ignore-geometry-prefs -geometry =600x600+100+100"

set URL=$1

# Modify this switch statement to add context-sensitive mappings.
# The map IDs can be any alphanumeric string (no spaces allowed).
switch ($URL)
case BlueSkyHome:
    set URL=http://www.blue-sky.com/
    breaksw
case WebHelpHome:
    set URL=http://www.blue-sky.com/WebHelp/
    breaksw
case WebHelpHelp:
    set URL=http://www.blue-sky.com/WebHelp/Help.htm
    breaksw
case WebHelpInstall:
    set URL=http://www.blue-sky.com/WebHelp/Install.htm
    breaksw
case WebHelpUpdate:
    set URL=http://www.blue-sky.com/WebHelp/Update.htm
    breaksw
endsw

# First, we attempt to access an already-open instance of the browser
$BROWSER -remote "openURL($URL)" >& /dev/null

# If that fails, we launch a new instance
if ($status) then
	$BROWSER $NS_START_ARGS $URL &
endif


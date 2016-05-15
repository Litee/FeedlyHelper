# FeedlyHelper

Command-line utility for working with Feedly. At the moment I am using it to automatically mark low interest (engagement) items as read.

# How to use:

Go to Feedly developer zone, get user ID and authentication token: https://developer.feedly.com/v3/developer/

Run FeedlyHelper.exe without parameters - it will automatically create FeedlyHelper.ini file next to .exe. Save user ID and authToken into this file.

Finally, run FeedlyHelper.exe:

`FeedlyHelper.exe mark-as-read --category <category-name> [--engagement-less-than <engagement>] [--no-confirmation] [--interval-minutes <interval>] [--min-entry-age-days <age>]`

If --interval-minutes parameter is not specified then tool will exit after single run. Otherwise it will keep running.

Example: `FeedlyHelper mark-as-read --category "News" --engagement-less-than 100 --no-confirmation --interval-minutes 60 --min-entry-age-days 1`


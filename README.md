# MediaConverter

FFMpeg wrapper for batch-converting of media files

Supported formats:

  Video:
  
    mp4, mkv, avi, flv, mov 
    
  Audio:
  
    mp3, m4a, flac, wav, ogg

Usage:

  -c, --count                    - Calculate input files count.

  -i, --input                    - Input directory path, if not specified - using current directory.

  -f, --format                   - Required. Calculate input files count.

  -r, --reset                    - Flush compressed file hashes from cache.

  -m, --mark-bad-as-completed    - Mark bad files as completed.

  -s, --scan-only                - Scan only (no convert).

  -l, --limit                    - Limit files for converting.

  --help                         - Display this help screen.

  --version                      - Display version information.
  
Example:
  MediaConverter.ConsoleClient.exe -l 10 -f mp4 - search all supported video files and encode to mp4.

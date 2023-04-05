# MediaConverter

FFMpeg wrapper for batch-converting of media files.
Place FFMPEG to executable folder or to any system $PATH folder.

Supported formats:

  Video:
  
    mp4, mkv, avi, flv, mov, wmv, webm
    
  Audio:
  
    mp3, m4a, flac, wav, ogg

Usage:

  -c, --count                    - Calculate input files count.

  -i, --input                    - Input directory path, if not specified - using current directory.

  -f, --format                   - Required. Output format. Input files will be found by this type (Audio or Video).

  -r, --reset                    - Flush compressed file hashes (history) from cache.

  -m, --mark-bad-as-completed    - Mark bad files or non-convertable files as completed.

  -s, --scan-only                - Scan only (no convert).

  -l, --limit                    - Limit files for converting.

  --help                         - Display this help screen.

  --version                      - Display version information.
  
Example:
  MediaConverter.ConsoleClient.exe -l 10 -f mp4 - search all supported video files and convert 10 files to mp4.

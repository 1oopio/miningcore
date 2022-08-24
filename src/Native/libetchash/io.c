/*
  This file is part of etchash.

  etchash is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  etchash is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with etchash.  If not, see <http://www.gnu.org/licenses/>.
*/
/** @file io.c
 * @author Lefteris Karapetsas <lefteris@ethdev.com>
 * @date 2015
 */
#include "io.h"
#include <string.h>
#include <stdio.h>
#include <errno.h>

enum etchash_io_rc etchash_io_prepare(
	char const* dirname,
	etchash_h256_t const seedhash,
	FILE** output_file,
	uint64_t file_size,
	bool force_create
)
{
	char mutable_name[DAG_MUTABLE_NAME_MAX_SIZE];
	enum etchash_io_rc ret = ETCHASH_IO_FAIL;
	// reset errno before io calls
	errno = 0;

	// assert directory exists
	if (!etchash_mkdir(dirname)) {
		ETCHASH_CRITICAL("Could not create the etchash directory");
		goto end;
	}

	etchash_io_mutable_name(ETCHASH_REVISION, &seedhash, mutable_name);
	char* tmpfile = etchash_io_create_filename(dirname, mutable_name, strlen(mutable_name));
	if (!tmpfile) {
		ETCHASH_CRITICAL("Could not create the full DAG pathname");
		goto end;
	}

	FILE *f;
	if (!force_create) {
		// try to open the file
		f = etchash_fopen(tmpfile, "rb+");
		if (f) {
			size_t found_size;
			if (!etchash_file_size(f, &found_size)) {
				fclose(f);
				ETCHASH_CRITICAL("Could not query size of DAG file: \"%s\"", tmpfile);
				goto free_memo;
			}
			if (file_size != found_size - ETCHASH_DAG_MAGIC_NUM_SIZE) {
				fclose(f);
				ret = ETCHASH_IO_MEMO_SIZE_MISMATCH;
				goto free_memo;
			}
			// compare the magic number, no need to care about endianess since it's local
			uint64_t magic_num;
			if (fread(&magic_num, ETCHASH_DAG_MAGIC_NUM_SIZE, 1, f) != 1) {
				// I/O error
				fclose(f);
				ETCHASH_CRITICAL("Could not read from DAG file: \"%s\"", tmpfile);
				ret = ETCHASH_IO_MEMO_SIZE_MISMATCH;
				goto free_memo;
			}
			if (magic_num != ETCHASH_DAG_MAGIC_NUM) {
				fclose(f);
				ret = ETCHASH_IO_MEMO_SIZE_MISMATCH;
				goto free_memo;
			}
			ret = ETCHASH_IO_MEMO_MATCH;
			goto set_file;
		}
	}
	
	// file does not exist, will need to be created
	f = etchash_fopen(tmpfile, "wb+");
	if (!f) {
		ETCHASH_CRITICAL("Could not create DAG file: \"%s\"", tmpfile);
		goto free_memo;
	}
	// make sure it's of the proper size
	if (fseek(f, (long int)(file_size + ETCHASH_DAG_MAGIC_NUM_SIZE - 1), SEEK_SET) != 0) {
		fclose(f);
		ETCHASH_CRITICAL("Could not seek to the end of DAG file: \"%s\". Insufficient space?", tmpfile);
		goto free_memo;
	}
	if (fputc('\n', f) == EOF) {
		fclose(f);
		ETCHASH_CRITICAL("Could not write in the end of DAG file: \"%s\". Insufficient space?", tmpfile);
		goto free_memo;
	}
	if (fflush(f) != 0) {
		fclose(f);
		ETCHASH_CRITICAL("Could not flush at end of DAG file: \"%s\". Insufficient space?", tmpfile);
		goto free_memo;
	}
	ret = ETCHASH_IO_MEMO_MISMATCH;
	goto set_file;

	ret = ETCHASH_IO_MEMO_MATCH;
set_file:
	*output_file = f;
free_memo:
	free(tmpfile);
end:
	return ret;
}

/*
Copyright 2017 Coin Foundry (coinfoundry.org)
Authors: Oliver Weichhold (oliver@weichhold.com)
Permission is hereby granted, free of charge, to any person obtaining a copy of this software and
associated documentation files (the "Software"), to deal in the Software without restriction,
including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense,
and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so,
subject to the following conditions:
The above copyright notice and this permission notice shall be included in all copies or substantial
portions of the Software.
THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT
LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.
IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE
SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
*/

#include "sha3.h"
#include "internal.h"
#include "etchash.h"

extern "C" bool etchash_get_default_dirname(char* strbuf, size_t buffsize);

#ifdef _WIN32
#define MODULE_API __declspec(dllexport)
#else
#define MODULE_API
#endif

extern "C" MODULE_API uint64_t etchash_get_datasize_export(uint64_t const block_number)
{
	return etchash_get_datasize(block_number);
}

extern "C" MODULE_API uint64_t etchash_get_cachesize_export(uint64_t const block_number)
{
	return etchash_get_cachesize(block_number);
}

extern "C" MODULE_API etchash_light_t etchash_light_new_export(uint64_t block_number)
{
	return etchash_light_new(block_number);
}

extern "C" MODULE_API void etchash_light_delete_export(etchash_light_t light)
{
	etchash_light_delete(light);
}

extern "C" MODULE_API void etchash_light_compute_export(
	etchash_light_t light,
	etchash_h256_t const *header_hash,
	uint64_t nonce,
	etchash_return_value_t *result)
{
	*result = etchash_light_compute(light, *header_hash, nonce);
}

extern "C" MODULE_API etchash_full_t etchash_full_new_export(const char *dirname, etchash_light_t light, etchash_callback_t callback)
{
	uint64_t full_size = etchash_get_datasize(light->block_number);
	etchash_h256_t seedhash = etchash_get_seedhash(light->block_number);
	return etchash_full_new_internal(dirname, seedhash, full_size, light, callback);
}

extern "C" MODULE_API void etchash_full_delete_export(etchash_full_t full)
{
	etchash_full_delete(full);
}

extern "C" MODULE_API void etchash_full_compute_export(
	etchash_full_t full,
	etchash_h256_t const *header_hash,
	uint64_t nonce,
	etchash_return_value_t *result)
{
	*result = etchash_full_compute(full, *header_hash, nonce);
}

extern "C" MODULE_API void const* etchash_full_dag_export(etchash_full_t full)
{
	return etchash_full_dag(full);
}

extern "C" MODULE_API uint64_t etchash_full_dag_size_export(etchash_full_t full)
{
	return etchash_full_dag_size(full);
}

extern "C" MODULE_API etchash_h256_t etchash_get_seedhash_export(uint64_t block_number)
{
	return etchash_get_seedhash(block_number);
}

extern "C" MODULE_API bool etchash_get_default_dirname_export(char *buf, size_t buf_size)
{
	return etchash_get_default_dirname(buf, buf_size);
}

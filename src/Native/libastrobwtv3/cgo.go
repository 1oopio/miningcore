package main

// #cgo CFLAGS: -g -Wall
// #include <stdlib.h>
import "C"

import (
	"bytes"
	"encoding/binary"
	"io"
	"unsafe"
)

func main() {}

//export Hash
func Hash(input string) unsafe.Pointer {
	// calculate the hash
	hash := AstroBWTv3([]byte(input))
	// allocate a new buffer
	buf := new(bytes.Buffer)
	// write the length of the hash to the buffer (incl. the bytes to store the length itself)
	writeInt32(buf, int32(len(hash)+4))
	// write the hash to the buffer
	hashString := string(hash[:])
	writeString(buf, &hashString)
	// return the pointer to the buffer
	return C.CBytes(buf.Bytes())
}

//export FreeMem
func FreeMem(p unsafe.Pointer) {
	C.free(p)
}

func writeInt32(w io.Writer, value int32) {
	err := binary.Write(w, binary.LittleEndian, value)
	if err != nil {
		panic(err)
	}
}

func writeString(buf *bytes.Buffer, value *string) {
	if value == nil {
		err := binary.Write(buf, binary.LittleEndian, int32(-1))
		if err != nil {
			panic(err)
		}

	} else {
		err := binary.Write(buf, binary.LittleEndian, int32(len(*value)))
		if err != nil {
			panic(err)
		}
		_, err = buf.WriteString(*value)
		if err != nil {
			panic(err)
		}
	}
}

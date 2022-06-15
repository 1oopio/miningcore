package main

// #cgo CFLAGS: -g -Wall
import "C"

import (
	"unsafe"
)

func main() {}

//export Hash
func Hash(input unsafe.Pointer, inputLen int, output unsafe.Pointer) bool {
	// Input bytes from pointer
	inputBytes := make([]byte, inputLen)
	for i := 0; i < inputLen; i++ {
		inputBytes[i] = *(*byte)(unsafe.Pointer(uintptr(input) + uintptr(i)))
	}

	// calculate the hash
	hash := AstroBWTv3(inputBytes)

	// Hash to pointer
	for i := 0; i < 32; i++ {
		*(*byte)(unsafe.Pointer(uintptr(output) + uintptr(i))) = hash[i]
	}
	return true
}

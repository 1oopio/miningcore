.PHONY: all clean clean-secp256k1 autoconf make-secp256k1

CFLAGS = -g -Wall -c -fPIC -O2 -Wno-pointer-sign -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-discarded-qualifiers -Wno-unused-const-variable $(CPU_FLAGS) $(HAVE_FEATURE)
CXXFLAGS = -g -Wall -fPIC -fpermissive -O2 -Wno-char-subscripts -Wno-unused-variable -Wno-unused-function -Wno-strict-aliasing -Wno-sign-compare -std=c++11 $(CPU_FLAGS) $(HAVE_FEATURE)
LDFLAGS = -shared
LDLIBS = -lgmp
TARGET  = libnexapow.so

OBJECTS = exports.o schnorr.o

all: make-secp256k1 $(TARGET)

autoconf:
	cd ./secp256k1; ./autogen.sh; export CC="gcc -fPIC"; ./configure --enable-module-schnorr;

make-secp256k1: 
	$(MAKE) -C ./secp256k1

$(TARGET): $(OBJECTS)
	$(CXX) $(LDFLAGS) -o $@ $^ secp256k1/.libs/libsecp256k1.a $(LDLIBS)

clean-secp256k1: autoconf
	$(MAKE) -C ./secp256k1 clean

clean: clean-secp256k1
	$(RM) $(TARGET) $(OBJECTS)


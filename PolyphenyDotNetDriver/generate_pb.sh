#!/bin/sh

mkdir -p Generated
protoc -I=./prism --csharp_out=./Generated prism/org/polypheny/prism/*.proto --experimental_allow_proto3_optional=true

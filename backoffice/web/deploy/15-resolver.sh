#!/bin/sh
# Lets nginx look up the API's private name at request time (the API may start after the backoffice).
ns=$(awk '/^nameserver/ { print $2; exit }' /etc/resolv.conf)
case "$ns" in *:*) ns="[$ns]" ;; esac
echo "resolver ${ns:-127.0.0.11} ipv6=on valid=10s;" > /etc/nginx/conf.d/00-resolver.conf

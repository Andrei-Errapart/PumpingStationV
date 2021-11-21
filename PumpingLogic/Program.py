from pymodbus.client.sync import ModbusTcpClient as ModbusClient

import logging
logging.basicConfig()
log = logging.getLogger()
log.setLevel(logging.DEBUG)


def run():
    client = ModbusClient(host='127.0.0.1', port=502)
    client.connect()
    if client.socket:
        print 'Connected!'
        rq = client.write_register(1, 3)
        rr = client.read_coils(1,1)
        assert(rq.function_code < 0x80)     # test that we are not an error
        print "rq: %s" % rq
        assert(rr.bits[0] == True)          # test the expected value
        print "rr: %s" % rr
    else:
        print "Cannot connect!"        



print 'Starting...'
run()
print 'Finished!'
